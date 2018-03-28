using MonoTorrent.Client;
using MonoTorrent.Common;
using MonoTorrent.Client.Tracker;
using MonoTorrent.BEncoding;
using MonoTorrent.Client.Encryption;
using MonoTorrent.Client.Connections;
using MonoTorrent.Client.PieceWriters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.IO;

namespace ConsoleTorrentClient
{
    class Program
    {
        static string _programPath;
        static string _downloadPath;
        static string _fastResumeFile;
        static string _torrentPath;
        static ClientEngine _engine;
        static Top10Listener _listener;
        static TorrentManager _manager;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Please run this program with parameters");
                Console.WriteLine("<torrent path> <Download folder>");
                Console.ReadKey();
                return;
            }
            _programPath = Environment.CurrentDirectory;
            _torrentPath = args[0];
            _downloadPath = args[1];
            _fastResumeFile = _programPath + "\temp.data";
            _listener = new Top10Listener(10);
            Console.CancelKeyPress +=
                delegate { exit(); };
            AppDomain.CurrentDomain.ProcessExit +=
                delegate { exit(); };
            AppDomain.CurrentDomain.UnhandledException +=
                delegate (object sender, UnhandledExceptionEventArgs e) { Console.WriteLine(e.ExceptionObject); exit(); };
            Thread.GetDomain().UnhandledException +=
                delegate (object sender, UnhandledExceptionEventArgs e) { Console.WriteLine(e.ExceptionObject); exit(); };
            doDownload();         
        }

        private static void doDownload()
        {
            int _port;
            _port = 31337;
            Torrent _torrent;
            EngineSettings _engineSetting = new EngineSettings();
            TorrentSettings _torrentDef = new TorrentSettings(5, 100, 0, 0);
            _engineSetting.SavePath = _downloadPath;
            _engineSetting.ListenPort = _port;
            _engine = new ClientEngine(_engineSetting);
            BEncodedDictionary _fastResume;

            try
            {
                _fastResume = BEncodedValue.Decode<BEncodedDictionary>
                    (File.ReadAllBytes(_fastResumeFile));
            }
            catch
            {
                _fastResume = new BEncodedDictionary();
            }
            try
            {
                _torrent = Torrent.Load(_torrentPath);
            }
            catch
            {
                Console.Write("Decoding error");
                _engine.Dispose();
                exit();
            }

            Console.WriteLine("Created by: {0}", _torrent.CreatedBy);
            Console.WriteLine("Creation date: {0}", _torrent.CreationDate);
            Console.WriteLine("Comment: {0}", _torrent.Comment);
            Console.WriteLine("Publish URL: {0}", _torrent.PublisherUrl);
            Console.WriteLine("Size: {0}", _torrent.Size);
            Console.WriteLine("Piece length: {0}", _torrent.PieceLength);
            Console.WriteLine("Piece count: {0}", _torrent.Pieces.Count);
            Console.WriteLine("");
            Console.WriteLine("Press any key for continue...");

            Console.ReadKey();

            if (_fastResume.ContainsKey(_torrent.InfoHash))
            {
                _manager = new TorrentManager(_torrent, _downloadPath, _torrentDef, new FastResume((BEncodedDictionary)_fastResume[_torrent.InfoHash]));
            }
            else
                _manager = new TorrentManager(_torrent, _downloadPath, _torrentDef);

            _engine.Register(_manager);

            _manager.TorrentStateChanged += delegate (object o, TorrentStateChangedEventArgs e)
            {
                lock (_listener)
                    _listener.WriteLine("Last status: " + e.OldState.ToString() + " Current status: " + e.NewState.ToString());
            };

            foreach (TrackerTier ttier in _manager.TrackerManager.TrackerTiers)
            {
                foreach (Tracker tr in ttier.Trackers)
                {
                    tr.AnnounceComplete += delegate (object sender, AnnounceResponseEventArgs e)
                    {
                        _listener.WriteLine(string.Format("{0}: {1}", e.Successful, e.Tracker.ToString()));
                    };
                }
            }
             _manager.Start();

         int i = 0;
         bool _running = true;

            StringBuilder _stringBuilder = new StringBuilder(1024);
            while (_running)
            {
                if ((i++) % 10 == 0)
                {
                    if (_manager.State == TorrentState.Stopped)
                    {
                        _running = false;
                        exit();
                    }

                    _stringBuilder.Remove(0, _stringBuilder.Length);

                    formatOutput(_stringBuilder, "Total Download Rate: {0:0.00}kB/sec", _engine.TotalDownloadSpeed / 1024.0);
                    formatOutput(_stringBuilder, "Total Upload Rate:   {0:0.00}kB/sec", _engine.TotalUploadSpeed / 1024.0);
                    formatOutput(_stringBuilder, "Disk Read Rate:      {0:0.00} kB/s", _engine.DiskManager.ReadRate / 1024.0);
                    formatOutput(_stringBuilder, "Disk Write Rate:     {0:0.00} kB/s", _engine.DiskManager.WriteRate / 1024.0);
                    formatOutput(_stringBuilder, "Total Read:         {0:0.00} kB", _engine.DiskManager.TotalRead / 1024.0);
                    formatOutput(_stringBuilder, "Total Written:      {0:0.00} kB", _engine.DiskManager.TotalWritten / 1024.0);
                    formatOutput(_stringBuilder, "Open Connections:    {0}", _engine.ConnectionManager.OpenConnections);

                    printLine(_stringBuilder);
                    formatOutput(_stringBuilder, "Name:            {0}", _manager.Torrent.Name);
                    formatOutput(_stringBuilder, "Progress:           {0:0.00}", _manager.Progress);
                    formatOutput(_stringBuilder, "Download Speed:     {0:0.00} kB/s", _manager.Monitor.DownloadSpeed / 1024.0);
                    formatOutput(_stringBuilder, "Upload Speed:       {0:0.00} kB/s", _manager.Monitor.UploadSpeed / 1024.0);
                    formatOutput(_stringBuilder, "Total Downloaded:   {0:0.00} MB", _manager.Monitor.DataBytesDownloaded / (1024.0 * 1024.0));
                    formatOutput(_stringBuilder, "Total Uploaded:     {0:0.00} MB", _manager.Monitor.DataBytesUploaded / (1024.0 * 1024.0));
                    formatOutput(_stringBuilder, "Tracker Status:     {0}", _manager.TrackerManager.CurrentTracker.State);
                    formatOutput(_stringBuilder, "Warning Message:    {0}", _manager.TrackerManager.CurrentTracker.WarningMessage);
                    formatOutput(_stringBuilder, "Failure Message:    {0}", _manager.TrackerManager.CurrentTracker.FailureMessage);

                    Console.Clear();
                    Console.WriteLine(_stringBuilder.ToString());
                    _listener.ExportTo(Console.Out);
                }
                System.Threading.Thread.Sleep(500);
            }
        }
        private static void printLine(StringBuilder sb)
        {
            formatOutput(sb, "", null);
            formatOutput(sb, "= = = = = = = = = = = = = = = = = = = = = = = = = = = = = =", null);
            formatOutput(sb, "", null);
        }
        private static void formatOutput(StringBuilder sb, string str, params object[] formatting)
        {
            if (formatting != null)
                sb.AppendFormat(str, formatting);
            else
                sb.Append(str);

            sb.AppendLine();
        }
        private static void exit()
        {
            BEncodedDictionary fastResume = new BEncodedDictionary();

            WaitHandle handle = _manager.Stop(); ;

            fastResume.Add(_manager.Torrent.InfoHash, _manager.SaveFastResume().Encode());

            File.WriteAllBytes(_fastResumeFile, fastResume.Encode());

            _engine.Dispose();

            foreach (TraceListener lst in Debug.Listeners)
            {
                lst.Flush();
                lst.Close();
            }

            System.Threading.Thread.Sleep(2000);
        }
    }
}
