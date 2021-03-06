﻿using System;
using System.Collections.Generic;
using System.IO;

using theori.Platform.Windows;

namespace theori.Core30
{
    sealed class ConsoleLoggerImpl : ILoggerImpl
    {
        public void Log(LogEntry entry)
        {
            string priority = entry.Priority.ToString();
            Console.WriteLine($"[{ entry.When.ToUniversalTime().TimeOfDay }]({ priority }) { new string('.', 10 - priority.Length) } : { entry.Message }");
        }
    }

    sealed class FileLoggerImpl : ILoggerImpl
    {
        private readonly string m_fileName;

        private static readonly List<string> lines = new List<string>();
        private static readonly object flushLock = new object();

        public FileLoggerImpl(string fileName)
        {
            m_fileName = fileName;
            lock (flushLock)
            {
                File.Delete(m_fileName);
                File.WriteAllText(m_fileName, "");
            }
        }

        public void Log(LogEntry entry)
        {
            string priority = entry.Priority.ToString();
            lines.Add($"[{ entry.When }]({ priority }) { new string('.', 10 - priority.Length) } : { entry.Message }");
        }

        public void Flush()
        {
            if (lines.Count == 0) return;

            lock (flushLock)
            {
                int count = lines.Count;
                using (var writer = new StreamWriter(File.Open(m_fileName, FileMode.Append)))
                {
                    for (int i = 0; i < count; i++)
                        writer.WriteLine(lines[i]);
                }
                lines.RemoveRange(0, count);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Logger.AddLogger(new ConsoleLoggerImpl());
            Logger.AddLogger(new FileLoggerImpl("theori-log.txt"));

            if (RuntimeInfo.IsWindows)
            {
                new WindowsPlatform().LoadLibrary("x64/SDL2.dll");
            }

            using var host = Host.GetSuitableHost();
            host.Initialize();

            host.Run(new TheoriClient());
        }
    }
}
