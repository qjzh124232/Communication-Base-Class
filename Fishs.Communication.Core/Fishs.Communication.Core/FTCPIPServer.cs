using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net;

namespace Fishs.Communication.TCPIP
{
    public class FTCPIPServer:IDisposable
    {
        protected static bool IsClose;

        protected Thread ListenThread;
        protected Thread ParsingMesgThread;
        protected Socket Listener;
        protected AutoResetEvent ListenCycle;
        protected AutoResetEvent ParsingCycle;

        public delegate byte[] ReplyMesgEventHandle(byte[] ReceiveMesg);
        public event ReplyMesgEventHandle ReplyMesgEvent;

        #region ServerPort
        /// <summary>
        /// 端口号
        /// </summary>
        public int ServerPort
        {
            get;
            private set;
        }
        #endregion

        #region MaxClients
        /// <summary>
        /// Listen 的最大客户端连接数，目前超出不阻止
        /// 默认为10
        /// 后期增加客户端数量控制，超过当前最大数量禁止连接
        /// </summary>
        public int MaxClients
        {
            get;
            set;
        }
        #endregion

        #region SendBufferSize
        /// <summary>
        /// 服务端接收发送的缓存区大小
        /// 默认8K
        /// </summary>
        public int SendBufferSize
        {
            get;
            set;
        }
        #endregion

        #region ReceiveBufferSize
        public int ReceiveBufferSize
        {
            get; set;
        }
        #endregion

        #region ClientList
        /// <summary>
        /// 客户端队列
        /// </summary>
        public List<Socket> ClientList
        {
            get;
            private set;
        }
        #endregion

        #region Constructor
        public FTCPIPServer(int ServerPort)
        {
            this.ServerPort = ServerPort;
            IsClose = false;
            ClientList = new List<Socket>();
            MaxClients = 10;
            SendBufferSize = 8192;
            ReceiveBufferSize = 8192;

            ListenCycle = new AutoResetEvent(false);
            ParsingCycle = new AutoResetEvent(false);

            ListenThread = new Thread(new ThreadStart(Listening)) { IsBackground = true };
            ListenThread.Start();
        }
        public FTCPIPServer(int ServerPort,int Backlog)
            :this(ServerPort)
        {
            this.MaxClients = Backlog;
        }
        #endregion

        #region protected void Listening()
        protected void Listening()
        {
            Listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Listener.Bind(new IPEndPoint(IPAddress.Any, ServerPort));
            Listener.Listen(MaxClients);

            Socket clientSocket;
            while (!IsClose)
            {
                clientSocket = Listener.Accept();
                if (clientSocket != null)
                {
                    ParsingMesgThread = new Thread(new ParameterizedThreadStart(ParsingMesg)) { IsBackground = true };
                    ParsingMesgThread.Start(clientSocket);
                }
                ListenCycle.WaitOne(10);
            }
        }
        #endregion

        #region protected void ParsingMesg(object clientSocket)
        protected void ParsingMesg(object clientSocket)
        {
            Socket client = clientSocket as Socket;
            string ReceiveValue = string.Empty;

            client.ReceiveBufferSize = ReceiveBufferSize;
            client.SendBufferSize = SendBufferSize;

            byte[] ReceiveByte;
            byte[] LengthByte = new byte[4];

            int iCount = 0;
            bool IsThisClose = false;

            try
            {
                while (!IsClose && IsThisClose)
                {
                    if (client != null && client.RemoteEndPoint != null)
                    {
                        int numBytes = client.Receive(LengthByte);
                        if (numBytes == 0)
                        {
                            iCount++;
                            if (iCount > 100)
                            {
                                IsThisClose = true;
                            }
                            ParsingCycle.WaitOne(100);
                            continue;
                        }

                        int length = BitConverter.ToInt32(LengthByte, 0);
                        if (length > 0)
                        {
                            iCount = 0;
                            ReceiveByte = new byte[length];

                            int tempLength = length;
                            int pos = 0;
                            while (tempLength > 0)
                            {
                                numBytes = client.Receive(ReceiveByte, pos, tempLength, SocketFlags.None);
                                tempLength -= numBytes;
                                pos += numBytes;
                            }

                            byte[] ReplyMesg = ReplyMesgEvent(WrapperReplyMesg(ReceiveByte));
                            int ReplyLength = client.Send(ReplyMesg);
                        }
                    }
                    ParsingCycle.WaitOne(13);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if(client!=null)
                {
                    client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    client.Dispose();
                }
                ReceiveByte = null;
                LengthByte = null;
                GC.Collect();
            }
        }
        #endregion

        #region protected byte[] WrapperReplyMesg(byte[] ReplyMesg)
        /// <summary>
        /// 封装返回值，在返回值数据前增加4个字节，用来存储该返回值的长度
        /// </summary>
        /// <param name="ReplyMesg"></param>
        /// <returns></returns>
        protected byte[] WrapperReplyMesg(byte[] ReplyMesg)
        {
            byte[] ReturnValue = null;
            if (ReplyMesg != null && ReplyMesg.Length > 0)
            {
                ReturnValue = new byte[ReplyMesg.Length + 4];
                byte[] MesgLength = BitConverter.GetBytes(ReplyMesg.Length);

                MesgLength.CopyTo(ReturnValue, 0);
                ReplyMesg.CopyTo(ReturnValue, 4);

                MesgLength = null;
            }
            return ReturnValue;
        }
        #endregion

        #region public void Dispose()
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            IsClose = true;
            ListenCycle.Set();
            ParsingCycle.Set();

            if (ClientList != null)
            {
                ClientList.Clear();
            }
            if (ListenThread != null)
            {
                ListenThread.Join(100);
                ListenThread.Abort();
                ListenThread = null;
            }
            if (ParsingMesgThread != null)
            {
                ParsingMesgThread.Join(100);
                ParsingMesgThread.Abort();
                ParsingMesgThread = null;
            }
            if (Listener != null)
            {
                Listener.Shutdown(SocketShutdown.Both);
                Listener.Close();
                Listener.Dispose();
            }
            
            ListenCycle.Close();
            ParsingCycle.Close();

            ListenCycle.Dispose();
            ParsingCycle.Dispose();

            GC.Collect();
        }
        #endregion
    }
}
