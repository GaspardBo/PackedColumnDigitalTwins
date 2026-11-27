using System;
using System.Runtime.InteropServices;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace PackedColumn
{
    using System;
    using System.Runtime.InteropServices;
    using IStream = System.Runtime.InteropServices.ComTypes.IStream;

    namespace PackedColumn
    {
        [ComImport, Guid("00000109-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPersistStream
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load(IStream pStm);
            void Save(IStream pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
            void GetSizeMax(out long pcbSize);
        }

        [ComImport, Guid("7FD52380-4E07-101B-AE2D-08002B2EC713"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPersistStreamInit
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load(IStream pStm);
            void Save(IStream pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
            void GetSizeMax(out long pcbSize);
            void InitNew();
        }
    }
}
