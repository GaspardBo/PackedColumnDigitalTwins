# Rebuild table rows from word positions (words within 3 pt vertically = one row).
# Usage: python -I extract_mt_rows.py <pymupdf dir> <pdf> <first page> <last page> <out.txt>
import sys; sys.path.insert(0, sys.argv[1]); import pymupdf
d = pymupdf.open(sys.argv[2]); p0, p1 = int(sys.argv[3]), int(sys.argv[4])
with open(sys.argv[5], 'w', encoding='utf-8') as o:
    for i in range(p0-1, p1):
        lines, cur, y = [], [], None
        for w in sorted(d[i].get_text('words'), key=lambda w: (w[1], w[0])):
            if y is None or abs(w[1]-y) > 3:
                if cur: lines.append(cur)
                cur, y = [w], w[1]
            else: cur.append(w)
        if cur: lines.append(cur)
        for l in lines:
            o.write(f'p{i+1}\t' + ' '.join(x[4] for x in sorted(l, key=lambda w: w[0])) + '\n')
