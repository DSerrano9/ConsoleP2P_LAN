using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;


namespace P2PConsoleApp_LAN
{
    class Program
    {
        private static Task ReceiverTask;
        private static NetworkStream NetworkStream;
        private static CancellationTokenSource TokenSource;


        #region Configuration
        private static void Configuration(string[] args)
        {
            switch ((args.Length == 2)
                    ? args[0].Trim().ToUpper()
                    : "default")
            {
                case "CONNECT": { NetworkStream = new NetworkStream(RunAsClient(args[1]), true); break; }
                case "LISTEN": { NetworkStream = new NetworkStream(RunAsServer(args[1]), true); break; }
                default: { throw new Exception("Failed to start correctly"); }
            }
            return;
        }
        private static IPEndPoint ConvertToIPEndPoint(string str)
        {
            ushort port = 0;
            string err = null;
            IPAddress ipAddr = null;
            string[] sockAddr = null;
            Action<string>[] actions = new Action<string>[]
            {
                (s) => { ipAddr = ConvertToIPAddress(s); },
                (s) => { port = ConvertToPortNumber(s); }
            };


            sockAddr = str?.Split(new char[] { ':' }, actions.Length);
            for (int i = 0; i < actions.Length; i++)
            {
                try
                {
                    actions[i]((sockAddr == null) || (i == sockAddr.Length)
                        ? null
                        : sockAddr[i]);
                }
                catch (Exception e)
                {
                    err = (err == null)
                        ? e.Message
                        : err += ':' + e.Message;
                }
            }
            return (err == null)
               ? new IPEndPoint(ipAddr, port)
               : throw new Exception(err);
        }
        private static IPAddress ConvertToIPAddress(string str)
        {
            bool success;
            byte[] octets = null;
            string[] ipAddr = null;


            if (success = (ipAddr = str?.Split('.'))?.Length == 4)
            {
                octets = new byte[ipAddr.Length];
                for (int i = 0; i < ipAddr.Length; i++)
                {
                    if (byte.TryParse(ipAddr[i], out octets[i]) == false)
                    { success = false; break; }
                }
            }
            return (success)
                ? new IPAddress(octets)
                : throw new Exception("Invalid IPv4 Address");
        }
        private static ushort ConvertToPortNumber(string str)
        {
            return (ushort.TryParse(str, out ushort port))
                ? port
                : throw new Exception("Invalid Port Number");
        }
        private static Socket RunAsClient(string str)
        {
            Socket sock = null;
            IPEndPoint sockAddr = ConvertToIPEndPoint(str);
            try
            {
                sock = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                Task task = sock.ConnectAsync(sockAddr);
                EllipsisAnimation(task.Wait, "Connecting", 30);

                if (task.Status == TaskStatus.Faulted)
                { throw task.Exception.InnerException; }
                else if (task.Status != TaskStatus.RanToCompletion)
                { throw new Exception("Connect Timeout"); }
                return sock;
            }
            catch { sock.Dispose(); throw; }
        }
        private static Socket RunAsServer(string str)
        {
            IPEndPoint sockAddr = ConvertToIPEndPoint(str);
            using (Socket sock = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp))
            {
                sock.Bind(sockAddr);
                sock.Listen(1);

                Task<Socket> task = sock.AcceptAsync();
                EllipsisAnimation(task.Wait, "Listening", 30);

                if (task.Status == TaskStatus.Faulted)
                { throw task.Exception.InnerException; }
                else if (task.Status != TaskStatus.RanToCompletion)
                { throw new Exception("Listen Timeout"); }
                return task.Result;
            }
        }
        private static void EllipsisAnimation(Func<int, bool> wait, string str, int timeout)
        {
            Console.CursorVisible = false;
            while (timeout > 0)
            {
                byte i = 0;
                Console.Write(str);
                do
                {
                    i++;
                    Console.Write('.');
                    try
                    {
                        if (wait(1000) || (--timeout == 0))
                        { timeout = 0; break; }
                    }
                    catch { timeout = 0; break; }
                } while (i < 3);
                Console.CursorLeft -= (str.Length + i);
                Console.Write(new string(' ', (str.Length + i)));
                Console.CursorLeft -= (str.Length + i);
            }
            Console.CursorVisible = true;
            return;
        }
        #endregion

        #region Receiver
        private static void Receiver(CancellationToken token)
        {
            while (true)
            {
                for (byte i = 0; i < 36; i++)
                {
                    if (NetworkStream.DataAvailable)
                    {
                        string data = Receive();
                        if (data != null)
                        { StandardIOWrapper.WriteLine("Remote Host>  " + data); }
                    }
                    token.WaitHandle.WaitOne(5000);
                    token.ThrowIfCancellationRequested();
                }
                SendHelloPacket();
            }
        }
        private static string Receive()
        {
            int byteRcvd;
            StringBuilder sb = null;


            if ((byteRcvd = NetworkStream.ReadByte()) != 0x0)
            {
                sb = new StringBuilder();
                do
                {
                    sb.Append(Convert.ToChar(byteRcvd));
                } while ((byteRcvd = NetworkStream.ReadByte()) != 0x0);
            }
            return sb?.ToString();
        }
        private static void SendHelloPacket()
        {
            NetworkStream.Write(new byte[] { 0x0 }, 0, 1);
            return;
        }
        #endregion

        #region Sender
        private static void Sender()
        {
            while (true)
            {
                Console.Write("Local Host>  ");
                string str = StandardIOWrapper.ReadLine() ?? "EXIT";
                if (str.Trim().ToUpper() == "EXIT") { return; }
                Send(str);
            }
        }
        private static void Send(string str)
        {
            byte[] buf = new byte[str.Length + 1];

            Encoding.ASCII.GetBytes(str, 0,
                str.Length, buf, 0);

            buf[buf.Length - 1] = 0x0;
            NetworkStream.Write(buf, 0, buf.Length);
            return;
        }
        #endregion

        #region Shutdown
        private static void Shutdown(Exception e)
        {
            if (ReceiverTask != null)
            {
                if ((e == null) && ReceiverTask.IsFaulted)
                {
                    e = ReceiverTask.Exception.Flatten().InnerException;
                }
                else if (!ReceiverTask.IsCompleted)
                {
                    ShutdownReceiver();
                }
                NetworkStream.Dispose();
            }   
            
            if (e != null)
            {
                Console.WriteLine("Error: " + e.Message);
            }
            return;
        }
        private static void Shutdown()
        {
            Shutdown(null);
            return;
        }
        private static void ShutdownReceiver()
        {
            TokenSource.Cancel();
            try { ReceiverTask.Wait(); }
            catch { }
            return;
        }
        #endregion

        static void Main(string[] args)
        {
            try
            {
                Configuration(args);
                Console.WriteLine();
                TokenSource = new CancellationTokenSource();
                ReceiverTask = Task.Delay(1000).ContinueWith(
                    (antecedent) =>
                    { Receiver(TokenSource.Token); },
                    TaskContinuationOptions.LongRunning).ContinueWith(
                    (antecedent) =>
                    {
                        StandardIOWrapper.AbortReadLine = true;
                        throw antecedent.Exception;
                    },
                    TokenSource.Token);
                Sender();
                Shutdown();
            }
            catch (Exception e) { Shutdown(e); }
        }
    }
}
