using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Protocol
{
    public enum WATCHER
    {
        HEARTBEAT = 0,
        WENUM_WATCHER_INFO = (1 + 12),//监视器消息
        WENUM_FILEOPERATION_ORDER = (1 + 13),//文件操作指令
    }

    [StructLayoutAttribute(LayoutKind.Sequential, Pack = 1)]
    public struct MSG_PROTOCOL_HEADER
    {
        public ushort usLen;
        public ushort usMessageID;
        public ushort usSourseDevID;
        public ushort usDestiDevID;
        public uint ulTime;
    }

    public struct watcher_info
    {
        public MSG_PROTOCOL_HEADER MsgHeader;
        public byte bitvector1;
        //[MarshalAsAttribute(UnmanagedType.LPStr)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string Description;
        public byte IsSpeak
        {
            get
            {
                return ((byte)((this.bitvector1 & 3u)));
            }
            set
            {
                this.bitvector1 = ((byte)((value | this.bitvector1)));
            }
        }
        public byte ShowGrowl
        {
            get
            {
                return ((byte)(((this.bitvector1 & 12u) / 4)));
            }
            set
            {
                this.bitvector1 = ((byte)(((value * 4) | this.bitvector1)));
            }
        }
        public byte Type
        {
            get
            {
                return ((byte)(((this.bitvector1 & 240u) / 16)));
            }
            set
            {
                this.bitvector1 = ((byte)(((value * 16) | this.bitvector1)));
            }
        }
    }
}
