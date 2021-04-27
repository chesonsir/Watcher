using NLog;
using NLog.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Watcher.WorkerService.Helper
{
    /// <summary>
    /// 项目日志封装
    /// </summary>
    public class LogHelper
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); //初始化日志类

        /// <summary>
        /// 日志状态枚举
        /// </summary>
        private enum LogState
        {
            /// <summary>
            /// 用户已登录
            /// </summary>
            NLogin,
            /// <summary>
            /// 用户未登录
            /// </summary>
            YLogin,
        }
        /// <summary>
        /// 日志类型枚举
        /// </summary>
        public enum LogType
        {
            Info,
            Debug,
            Trace,
            Error,
            Warn,
            Fatal
        }

        /// <summary>
        /// 静态构造函数
        /// </summary>
        static LogHelper()
        {
            //初始化配置日志
            LogManager.Configuration = new XmlLoggingConfiguration(AppDomain.CurrentDomain.BaseDirectory + "\\Helper\\Nlog.config");
        }

        /// <summary>
        /// 日志写入通用方法(建议使用)
        /// </summary>
        /// <param name="msg">日志内容</param>
        /// <param name="logType"> 日志类别
        ///     类别: 1.Debug
        ///           2.Info
        ///           3.Error
        ///           4.Fatal
        ///           5.Warn
        /// </param>
        /// <param name="loginState">登录状态  true:有用户登录信息 false 无用户登录信息</param>
        /// <remarks>
        ///     注：默认类型为Info 可以配置其他日志 logType用于反射 规则一定要准确
        ///     例:  1.默认日志 LogWriter("test log");   正常的业务日志
        ///          2.异常日志 LogWriter("test log","Fatal");  try catch 里请使用这个日志级别
        ///     
        /// </remarks>
        public static void LogWriter(String msg, LogType logType = LogType.Debug, bool loginState = true)
        {
            try
            {
                String logMethod = "";  //调用者类名和方法名
                if (logType == LogType.Fatal)
                {
                    StackTrace trace = new StackTrace();
                    //获取是哪个类来调用的  
                    String invokerType = trace.GetFrame(1).GetMethod().DeclaringType.Name;
                    //获取是类中的那个方法调用的  
                    String invokerMethod = trace.GetFrame(1).GetMethod().Name;
                    logMethod = invokerType + "." + invokerMethod + " | ";
                }

                //String IP = HttpContext.Current.Request.UserHostAddress;   //获取IP
                String IP = "";
                                                                           //反射执行日志方法
                Type type = typeof(Logger);
                MethodInfo method = type.GetMethod(logType.ToString(), new Type[] { typeof(String) });
                if (loginState == true)
                {
                    //如果是登陆状态 可以记录用户的登陆信息 比如用户名,Id等
                    method.Invoke(logger, new object[] { logMethod + msg + " [ " + IP + " | " + LogState.NLogin + " ]" });
                }
                else
                {
                    method.Invoke(logger, new object[] { logMethod + msg + " [ " + IP + " | " + LogState.NLogin + " ]" });
                }
            }
            catch
            {
                //日志代码错误,直接记录日志
                Fatal(msg);
                Warn(msg);
            }
        }

        /// <summary>
        /// 调试日志
        /// </summary>
        /// <param name="msg">日志内容</param>
        public static void Debug(String msg)
        {
            logger.Debug(msg); 
        }

        /// <summary>
        /// 信息日志
        /// </summary>
        /// <param name="msg">日志内容</param>
        /// <remarks>
        ///     适用大部分场景
        ///     1.记录日志文件
        /// </remarks>
        public static void Info(String msg)
        {
            logger.Info(msg);
        }

        /// <summary>
        /// 错误日志
        /// </summary>
        /// <param name="msg">日志内容</param>
        /// <remarks>
        ///     适用异常,错误日志记录
        ///     1.记录日志文件
        /// </remarks>
        public static bool Error(String msg)
        {
            logger.Error(msg); return false;
        }

        /// <summary>
        /// 最常见的记录信息，一般用于普通输出
        /// </summary>
        public static void Trace(String msg)
        {
            logger.Trace(msg);
        }

        /// <summary>
        /// 严重致命错误日志
        /// </summary>
        /// <param name="msg">日志内容</param>
        /// <remarks>
        ///     1.记录日志文件
        ///     2.控制台输出
        /// </remarks>
        public static bool Fatal(String msg)
        {
            logger.Fatal(msg); return false;
        }

        /// <summary>
        /// 警告日志
        /// </summary>
        /// <param name="msg">日志内容</param>
        /// <remarks>
        ///     1.记录日志文件
        ///     2.发送日志邮件
        /// </remarks>
        public static void Warn(String msg)
        {
            try
            {
                logger.Warn(msg);
            }
            catch { }
        }
    }
}
