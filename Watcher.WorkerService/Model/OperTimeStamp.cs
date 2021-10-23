using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Model
{
    /// <summary>
    /// 文件被操作的时间信息类，用来判断文件是否被正常修改（OnChanged）
    /// </summary>
    public class OperTimeStamp
    {
        public OperTimeStamp(DateTime dtnow, string filename, string fullpath)
        {
            this.DtNow = dtnow;
            this.FileName = filename;
            this.FullPath = fullpath;
        }
        /// <summary>
        /// 操作时间戳
        /// </summary>
        private DateTime dtnow;
        public DateTime DtNow
        {
            get { return dtnow; }
            set { dtnow = value; }
        }

        /// <summary>
        /// 文件名
        /// </summary>
        private string filename;
        public string FileName
        {
            get { return filename; }
            set { filename = value; }
        }

        /// <summary>
        /// 完整路径
        /// </summary>
        private string fullPath;
        public string FullPath
        {
            get { return fullPath; }
            set { fullPath = value; }
        }
    }
}
