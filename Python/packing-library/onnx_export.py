# onnx_export: write a level 2 block (json_format.md) as an .onnx model, so the DLL can run it with ONNX Runtime
#
# every model has the same interface, whatever its form (linear or network) and whichever inputs it uses:
#   input   X                  double [N, 2]  columns: Re_L, x_flood (as defined in json_format.md)
#   outputs log_correction     double [N, 1]  y
#           correction_factor  double [N, 1]  exp(y): corrected output = level 1 output x correction_factor
#           in_valid_range     bool   [N, 1]  false when Re_L or x_flood is outside valid_range (inclusive);
#                                             the caller must then use level 1 for that point
# an input the block does not use (a one-input linear block) is scaled with mean 0, sd 1 and gets coefficient 0.
# the block's metadata (output, form, valid_range, level1_constants, ...) is stored in the model's metadata_props;
# level1_constants says which constants the correction was trained on top of: if the packing's constants differ,
# do not apply the model.

import json
import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

INPUTS = ['Re_L', 'x_flood']
OPSET = 17
IR_VERSION = 8  # readable by ONNX Runtime 1.10 and later


def _const(name, array):
    return numpy_helper.from_array(np.asarray(array, dtype=np.float64), name)


def block_to_model(block, packing_id=None):
    for name in block['inputs']:
        if name not in INPUTS:
            raise ValueError(f"unknown level 2 input '{name}'")

    # scaling over both inputs; unused inputs are left as they are and get no weight
    mean = np.zeros(len(INPUTS))
    sd = np.ones(len(INPUTS))
    for i, name in enumerate(block['inputs']):
        mean[INPUTS.index(name)] = block['scaling']['mean'][i]
        sd[INPUTS.index(name)] = block['scaling']['sd'][i]
    lo = np.array([[block['valid_range'][n][0] for n in INPUTS]])
    hi = np.array([[block['valid_range'][n][1] for n in INPUTS]])

    inits = [_const('mean', mean[None, :]), _const('sd', sd[None, :]), _const('lo', lo), _const('hi', hi)]
    nodes = [helper.make_node('Sub', ['X', 'mean'], ['Xc']),
             helper.make_node('Div', ['Xc', 'sd'], ['Z'])]

    if block['form'] == 'linear':
        coef = np.zeros((len(INPUTS), 1))
        for i, name in enumerate(block['inputs']):
            coef[INPUTS.index(name), 0] = block['linear']['coef'][i]
        inits += [_const('coef', coef), _const('intercept', [[block['linear']['intercept']]])]
        nodes += [helper.make_node('MatMul', ['Z', 'coef'], ['Zc']),
                  helper.make_node('Add', ['Zc', 'intercept'], ['log_correction'])]
    elif block['form'] == 'network':
        if block['inputs'] != INPUTS:
            raise ValueError('a network block must use the inputs ' + ', '.join(INPUTS) + ' in that order')
        members = block['network']['members']
        outs = []
        for m, member in enumerate(members):
            h = 'Z'
            n = len(member['weights'])
            for k in range(n):
                # weights[k]: rows = output nodes, so h_next = h W^T + b  (Gemm with transB)
                w, b = f'm{m}_W{k}', f'm{m}_b{k}'
                inits += [_const(w, member['weights'][k]), _const(b, member['biases'][k])]
                out = f'm{m}_g{k}'
                nodes.append(helper.make_node('Gemm', [h, w, b], [out], transB=1))
                if k < n - 1:
                    nodes.append(helper.make_node('Relu', [out], [f'm{m}_h{k}']))
                    out = f'm{m}_h{k}'
                h = out
            outs.append(h)
        inits.append(_const('inv_members', [[1.0 / len(members)]]))
        nodes += [helper.make_node('Sum', outs, ['member_sum']),
                  helper.make_node('Mul', ['member_sum', 'inv_members'], ['log_correction'])]
    else:
        raise ValueError(f"unknown level 2 form '{block['form']}'")

    nodes += [helper.make_node('Exp', ['log_correction'], ['correction_factor']),
              helper.make_node('GreaterOrEqual', ['X', 'lo'], ['above_lo']),
              helper.make_node('LessOrEqual', ['X', 'hi'], ['below_hi']),
              helper.make_node('And', ['above_lo', 'below_hi'], ['inside']),
              helper.make_node('Cast', ['inside'], ['inside_d'], to=TensorProto.DOUBLE),
              helper.make_node('ReduceMin', ['inside_d'], ['inside_min'], axes=[1], keepdims=1),
              helper.make_node('Cast', ['inside_min'], ['in_valid_range'], to=TensorProto.BOOL)]

    graph = helper.make_graph(
        nodes, f"level2_{block['output']}",
        [helper.make_tensor_value_info('X', TensorProto.DOUBLE, ['N', len(INPUTS)])],
        [helper.make_tensor_value_info('log_correction', TensorProto.DOUBLE, ['N', 1]),
         helper.make_tensor_value_info('correction_factor', TensorProto.DOUBLE, ['N', 1]),
         helper.make_tensor_value_info('in_valid_range', TensorProto.BOOL, ['N', 1])],
        inits)
    model = helper.make_model(graph, producer_name='packing-library train.py',
                              opset_imports=[helper.make_opsetid('', OPSET)])
    model.ir_version = IR_VERSION
    model.doc_string = (f"Level 2 correction for {block['output']}: corrected = level 1 x correction_factor. "
                        f"Input X columns: {', '.join(INPUTS)}. Use level 1 where in_valid_range is false.")
    meta = {'output': block['output'], 'form': block['form'], 'inputs': ','.join(INPUTS),
            'block_inputs': ','.join(block['inputs']),
            'valid_range': json.dumps(block['valid_range']),
            'level1_constants': json.dumps(block['level1_constants']),
            'trained_on_runs': str(block['trained_on_runs'])}
    if packing_id:
        meta['packing_id'] = packing_id
    helper.set_model_props(model, meta)
    onnx.checker.check_model(model)
    return model


def write_block(block, path, packing_id=None):
    onnx.save(block_to_model(block, packing_id), path)
    return path


def check_against_block(block, path, points=50, seed=0):
    # optional: compare the .onnx model with packlib.apply_block at points inside and outside the valid range.
    # returns the largest relative difference of the correction factor, or None if onnxruntime is not installed
    try:
        import onnxruntime as ort
    except ImportError:
        return None
    import packlib
    rng = np.random.default_rng(seed)
    lo = np.array([block['valid_range'][n][0] for n in INPUTS])
    hi = np.array([block['valid_range'][n][1] for n in INPUTS])
    span = np.where(hi > lo, hi - lo, np.abs(lo) + 1)
    X = lo + rng.uniform(-0.2, 1.2, size=(points, len(INPUTS))) * span
    factor, inside = ort.InferenceSession(path, providers=['CPUExecutionProvider']).run(
        ['correction_factor', 'in_valid_range'], {'X': X})
    worst = 0.0
    for x, f, ok in zip(X, factor[:, 0], inside[:, 0]):
        expected = packlib.apply_block(block, dict(zip(INPUTS, x)))
        if (expected is None) == bool(ok):
            raise AssertionError(f'{path}: valid-range flag differs from apply_block at {x}')
        if expected is not None:
            worst = max(worst, abs(f / expected - 1))
    return worst
