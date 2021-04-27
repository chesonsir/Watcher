using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Helper
{
    public class FtpHelper
    {
        private readonly string _basePath;
        private readonly string _userName;
        private readonly string _passWord;
        public FtpHelper(string ftpBasePath, string userName, string passWord)
        {
            _basePath = ftpBasePath; _userName = userName; _passWord = passWord;
            if (!_basePath.EndsWith("/")) _basePath += "/";
        }

        /// <summary>
        /// 上传单个文件
        /// </summary>
        public void UpLoadFile(string fileName, string newName = null)
        {
            var fileInf = new FileInfo(fileName);
            var uri = _basePath + (newName ?? fileInf.Name);

            var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));
            reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
            reqFTP.KeepAlive = false;
            reqFTP.Method = WebRequestMethods.Ftp.UploadFile;
            reqFTP.UseBinary = true;
            reqFTP.ContentLength = fileInf.Length;
            var buffLength = 2048;
            var buff = new byte[buffLength];
            var fs = fileInf.OpenRead();
            try
            {
                var strm = reqFTP.GetRequestStream();
                var contentLen = fs.Read(buff, 0, buffLength);
                while (contentLen != 0)
                {
                    strm.Write(buff, 0, contentLen);
                    contentLen = fs.Read(buff, 0, buffLength);
                }
                strm.Close();
                fs.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件上传错误，{ex.Message}");
            }
        }

        /// <summary>
        /// 上传文件夹
        /// </summary>
        public void UpLoadDirectory(string sourcePath, string destinationPath)
        {
            try
            {
                sourcePath = sourcePath.EndsWith(@"\") ? sourcePath : sourcePath + @"\";
                destinationPath = destinationPath.EndsWith("/") ? destinationPath : destinationPath + "/";
                if (Directory.Exists(sourcePath))
                {
                    if (!DirectoryExist(destinationPath)) MakeDir(destinationPath);
                    foreach (string fls in Directory.GetFiles(sourcePath))
                    {
                        var flinfo = new FileInfo(fls);
                        UpLoadFile(fls, destinationPath + flinfo.Name);
                    }
                    foreach (string drs in Directory.GetDirectories(sourcePath))
                    {
                        var drinfo = new DirectoryInfo(drs);
                        UpLoadDirectory(drs, destinationPath + drinfo.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"{sourcePath}:文件夹上传错误，{ex.Message}");
            }
        }

        /// <summary>
        /// 下载单个文件
        /// </summary>
        public void Download(string filePath, string fileName)
        {
            var uri = _basePath + fileName;
            try
            {
                var outputStream = new FileStream(@$"{filePath}\{fileName}", FileMode.Create);

                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));
                reqFTP.Method = WebRequestMethods.Ftp.DownloadFile;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var ftpStream = response.GetResponseStream();
                var cl = response.ContentLength;
                var bufferSize = 2048;
                var buffer = new byte[bufferSize];

                var readCount = ftpStream.Read(buffer, 0, bufferSize);
                while (readCount > 0)
                {
                    outputStream.Write(buffer, 0, readCount);
                    readCount = ftpStream.Read(buffer, 0, bufferSize);
                }

                ftpStream.Close();
                outputStream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件下载错误，{ex.Message}");
            }
        }

        /// <summary>
        /// 删除单个文件
        /// </summary>
        public void Delete(string fileName)
        {
            var uri = _basePath + fileName;
            try
            {
                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));

                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                reqFTP.KeepAlive = false;
                reqFTP.Method = WebRequestMethods.Ftp.DeleteFile;

                var result = string.Empty;
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var size = response.ContentLength;
                var datastream = response.GetResponseStream();
                var sr = new StreamReader(datastream);
                result = sr.ReadToEnd();
                sr.Close();
                datastream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件删除错误，{ex.Message}");
            }
        }

        /// <summary>
        /// 删除文件夹
        /// </summary>
        public void RemoveDirectory(string folderName)
        {
            var uri = _basePath + folderName;
            try
            {
                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));

                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                reqFTP.KeepAlive = false;
                reqFTP.Method = WebRequestMethods.Ftp.RemoveDirectory;

                var result = string.Empty;
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var size = response.ContentLength;
                var datastream = response.GetResponseStream();
                var sr = new StreamReader(datastream);
                result = sr.ReadToEnd();
                sr.Close();
                datastream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件夹删除错误，{ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定目录下明细(包含文件和文件夹)
        /// </summary>
        public string[] GetFilesDetailList(string folderName = "")
        {
            var uri = _basePath + folderName;
            try
            {
                var result = new StringBuilder();
                var ftp = (FtpWebRequest)WebRequest.Create(new Uri(uri));
                ftp.Credentials = new NetworkCredential(_userName, _passWord);
                ftp.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
                var response = ftp.GetResponse();
                var reader = new StreamReader(response.GetResponseStream());
                var line = reader.ReadLine();
                if (line == null) return null;
                while (line != null)
                {
                    result.Append(line);
                    result.Append('\n');
                    line = reader.ReadLine();
                }
                if (!string.IsNullOrEmpty(result.ToString())) result.Remove(result.ToString().LastIndexOf('\n'), 1);
                reader.Close();
                response.Close();
                return result.ToString().Split('\n');
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件夹信息获取失败，{ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定目录下文件列表(仅文件)
        /// </summary>
        public string[] GetFileList(string filter, string folderName = "")
        {
            var uri = _basePath + folderName;
            try
            {
                var result = new StringBuilder();
                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(uri));
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                reqFTP.Method = WebRequestMethods.Ftp.ListDirectory;
                var response = reqFTP.GetResponse();
                var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);

                string line = reader.ReadLine();
                if (line == null) return null;
                while (line != null)
                {
                    if (filter.Trim() != string.Empty && filter.Trim() != "*.*")
                    {

                        string mask_ = filter.Substring(0, filter.IndexOf("*"));
                        if (line.Substring(0, mask_.Length) == mask_)
                        {
                            result.Append(line);
                            result.Append('\n');
                        }
                    }
                    else
                    {
                        result.Append(line);
                        result.Append('\n');
                    }
                    line = reader.ReadLine();
                }
                result.Remove(result.ToString().LastIndexOf('\n'), 1);
                reader.Close();
                response.Close();
                return result.ToString().Split('\n');
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件信息获取失败，{ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定目录下所有的文件夹列表(仅文件夹)
        /// </summary>
        public string[] GetDirectoryList(string folderName = "")
        {
            var directory = GetFilesDetailList(folderName);
            if (directory == null) return null;
            var m = string.Empty;
            foreach (var str in directory)
            {
                var dirPos = str.IndexOf("<DIR>");
                if (dirPos > 0)
                {
                    /*判断 Windows 风格*/
                    m += str[(dirPos + 5)..].Trim() + "\n";
                }
                else if (!string.IsNullOrEmpty(str) && str.Trim().Substring(0, 1).ToUpper() == "D")
                {
                    /*判断 Unix 风格*/
                    string dir = str[54..].Trim();
                    if (dir != "." && dir != "..")
                    {
                        m += dir + "\n";
                    }
                }
            }

            char[] n = new char[] { '\n' };
            return m.Split(n);
        }

        /// <summary>
        /// 判断指定目录下指定的子目录是否存在
        /// </summary>
        /// <param name="RemoteDirectoryName">指定的目录名</param>
        public bool DirectoryExist(string RemoteDirectoryName, string folderName = "")
        {
            var dirList = GetDirectoryList(folderName);
            if (dirList == null) return false;
            foreach (string str in dirList)
            {
                if (str.Trim() == RemoteDirectoryName.Trim())
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 判断指定目录下指定的文件是否存在
        /// </summary>
        /// <param name="RemoteFileName">远程文件名</param>
        public bool FileExist(string RemoteFileName, string folderName = "")
        {
            var fileList = GetFileList("*.*", folderName);
            if (fileList == null) return false;
            foreach (string str in fileList)
            {
                if (str.Trim() == RemoteFileName.Trim())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 创建文件夹
        /// </summary>
        public void MakeDir(string dirName)
        {
            var uri = _basePath + dirName;
            try
            {
                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(_basePath + dirName));
                reqFTP.Method = WebRequestMethods.Ftp.MakeDirectory;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var ftpStream = response.GetResponseStream();

                ftpStream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件信息获取失败，{ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定文件大小
        /// </summary>
        public long GetFileSize(string filename)
        {
            var uri = _basePath + filename;
            try
            {
                var reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(uri));
                reqFTP.Method = WebRequestMethods.Ftp.GetFileSize;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var ftpStream = response.GetResponseStream();
                var fileSize = response.ContentLength;

                ftpStream.Close();
                response.Close();
                return fileSize;
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:文件信息获取失败，{ex.Message}");
            }
        }

        /// <summary>
        /// 改名
        /// </summary>
        public void ReName(string currentFilename, string newFilename)
        {
            var uri = _basePath + currentFilename;
            try
            {
                var reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(_basePath + currentFilename));
                reqFTP.Method = WebRequestMethods.Ftp.Rename;
                reqFTP.RenameTo = newFilename;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(_userName, _passWord);
                var response = (FtpWebResponse)reqFTP.GetResponse();
                var ftpStream = response.GetResponseStream();

                ftpStream.Close();
                response.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"{uri}:重命名失败，{ex.Message}");
            }
        }

        /// <summary>
        /// 移动文件
        /// </summary>
        public void MovieFile(string currentFilename, string newDirectory)
        {
            ReName(currentFilename, newDirectory);
        }
    }
}
