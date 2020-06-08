﻿using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using LogPrinter;
using Emgu.CV;
using Emgu.CV.Structure;

namespace WinFormClient
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// 协议关键字
        /// </summary>
        public enum VideoTransferProtocolKey : byte
        {
            Header1 = 34,
            Header2 = 84,
            RSAKey = 104,
            BeginTransferVideo = 114,
            VideoTransfer = 204,
            PingSignal = 244,
            EndTransferVideo = 254
        }

        #region 静态字段
        const bool ifAtSamePC = true;
        const bool ifAtSameLAN = true;
        const string netAdapterName = "WLAN 2";

        const string clientIPAtSamePC = "127.0.0.1";
        const int clientPortTCPAtSamePC = 40007;
        const int clientPortUDPAtSamePC = 40008;
        const string serverIPAtSamePC = "127.0.0.1";

        static string clientIPAtSameLAN;
        const int clientPortTCPAtSameLAN = 40005;
        const int clientPortUDPAtSameLAN = 40006;
        const string serverIPAtSameLAN = "192.168.1.117"; // 应该是192.168.1.11 此处为测试PC

        static string clientIPAtWAN;
        const int clientPortTCPAtWAN = 40005;
        const int clientPortUDPAtWAN = 40006;
        const string serverIPAtWAN = "202.120.48.24"; // 路由器的公网IP

        const int serverPortTCPAtAll = 40005; // 端口转发应该设置同一端口
        const int serverPortUDPAtAll = 40006; //端口转发应该设置同一端口

        const byte clientDeviceIndex = 1;
        EndPoint serverEndPoint = new IPEndPoint(0, 0);

        Socket tcpTransferSocket;
        bool ifTcpConnectionEstablished = false;
        const int tcpTransferSocketSendTimeOut = 500;
        const int tcpTransferSocketInterval = 120;
        System.Timers.Timer tcpSendClocker = new System.Timers.Timer(tcpTransferSocketInterval);
        Task tcpTransferSendTask;
        CancellationTokenSource tcpTransferCancel;
        Queue<VideoTransferProtocolKey> tcpSendQueue = new Queue<VideoTransferProtocolKey>(100);
        private static readonly object queueLocker = new object();
        const int sleepMsForQueueSend = 10;

        Socket udpTransferSocket;
        CancellationTokenSource udpTransferCancel;
        Task udpTransferRecieveTask;
        string publicKey;
        string privateKey;
        const int udpTransferSocketSendMaxTimeOut = 5000;
        const int udpTransferSocketSendTimeOut = 3000;
        const int keyLength = 1024;
        const int maxVideoByteLength = 60000;
        byte lastReceivePackIndex = 0;
        byte bufferReceivePackIndex = 0;
        List<byte[]> bufferReceivePackContent = new List<byte[]>(byte.MaxValue);
        List<byte> bufferReceivePackNum = new List<byte>(byte.MaxValue);

        bool ifFindAddress = false;
        #endregion

        public Form1()
        {
            InitializeComponent();

            // 检查环境
            if (!Functions.CheckEnvironment()) return;
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client starts with successful checked.");

            // 装上TCP定时器
            tcpSendClocker.AutoReset = false;
            tcpSendClocker.Elapsed += tcpSendClocker_Elapsed;
            camera.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.Fps, 10);
            camera.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.FrameHeight, 1080);
            camera.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.FrameWidth, 1920);

            tcpSendClocker.Start();

            // 获取RSA密钥
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024);
            publicKey = rsa.ToXmlString(false);
            privateKey = rsa.ToXmlString(true);

            // 刷新公共密钥
            using (AesCryptoServiceProvider tempAes = new AesCryptoServiceProvider())
            {
                tempAes.GenerateKey();
                tempAes.GenerateIV();
                commonKey = tempAes.Key;
                commonIV = tempAes.IV;
            }

            // 获得当前client的IP地址
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.Name == netAdapterName)
                {
                    UnicastIPAddressInformationCollection unicastIPAddressInformation = adapter.GetIPProperties().UnicastAddresses;
                    foreach (var item in unicastIPAddressInformation)
                    {
                        if (item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            if (!ifAtSamePC && ifAtSameLAN)
                            {
                                clientIPAtSameLAN = item.Address.ToString();
                            }
                            else
                            {
                                clientIPAtWAN = item.Address.ToString();
                            }
                            ifFindAddress = true;
                            break;
                        }
                    }
                }
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            //if (!ifFindAddress)
            //{
            //    this.Close();
            //    return;
            //}
        }

        private void beginBtn_Click(object sender, EventArgs e)
        {
            // TCP连接已经建立就退出
            if (ifTcpConnectionEstablished) return;

            // 重新建立新的TCP连接
            tcpTransferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpTransferSocket.Bind(new IPEndPoint(IPAddress.Parse(ifAtSamePC ? clientIPAtSamePC : (ifAtSameLAN ? clientIPAtSameLAN : clientIPAtWAN)), ifAtSamePC ? clientPortTCPAtSamePC : (ifAtSameLAN ? clientPortTCPAtSameLAN : clientPortTCPAtWAN)));
            tcpTransferSocket.SendTimeout = tcpTransferSocketSendTimeOut;
            tcpTransferSocket.ReceiveTimeout = tcpTransferSocketSendTimeOut;
            try
            {
                tcpTransferSocket.Connect(new IPEndPoint(IPAddress.Parse(ifAtSamePC ? serverIPAtSamePC : (ifAtSameLAN ? serverIPAtSameLAN : serverIPAtWAN)), serverPortTCPAtAll));
            }
            catch (SocketException ex)
            {
                tcpTransferSocket.Close();
                if (ex.SocketErrorCode == SocketError.ConnectionRefused || ex.SocketErrorCode == SocketError.TimedOut)
                {
                    Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp connection can not established.", ex);
                    MessageBox.Show("网络连接失败！\r\n问题：" + ex.Message);
                    return;
                }
                else
                {
                    Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "Not deal exception.", ex);
                    throw ex;
                }
            }

            ifTcpConnectionEstablished = true;
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp connection has been established.");

            // 开始允许TCP传输socket发送队列内的数据
            tcpTransferCancel = new CancellationTokenSource();
            tcpTransferSendTask = new Task(() => TcpTransferSendTaskWork(tcpTransferCancel.Token));
            tcpTransferSendTask.Start();
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp tranfer can send datas.");

            // 发送公钥
            SendCmd(VideoTransferProtocolKey.RSAKey);

            // 开始接收视频
            SendCmd(VideoTransferProtocolKey.BeginTransferVideo);

            // 开始允许UDP传输socket接收数据
            udpTransferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpTransferSocket.Bind(new IPEndPoint(IPAddress.Parse(ifAtSamePC ? clientIPAtSamePC : (ifAtSameLAN ? clientIPAtSameLAN : clientIPAtWAN)), ifAtSamePC ? clientPortUDPAtSamePC : (ifAtSameLAN ? clientPortUDPAtSameLAN : clientPortUDPAtWAN)));
            udpTransferSocket.ReceiveTimeout = udpTransferSocketSendMaxTimeOut;
            udpTransferCancel = new CancellationTokenSource();
            udpTransferRecieveTask = new Task(() => UDPTransferRecieveTaskWork(udpTransferCancel.Token));
            udpTransferRecieveTask.Start();
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client udp tranfer can recieve datas.");

            // 心跳发送定时器打开
            tcpSendClocker.Start();
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp begin to beat.");
        }

        private void stopBtn_Click(object sender, EventArgs e)
        {
            // TCP连接未建立就退出
            if (!ifTcpConnectionEstablished) return;

            // 发送停止接收视频
            SendCmd(VideoTransferProtocolKey.EndTransferVideo);
        }

        /// <summary>
        /// TCP发送队列数据任务
        /// </summary>
        /// <param name="cancelFlag">停止标志</param>
        private void TcpTransferSendTaskWork(CancellationToken cancelFlag)
        {
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp transfer begins to send datas.");

            while (true)
            {
                if (cancelFlag.IsCancellationRequested) break;

                Thread.Sleep(sleepMsForQueueSend);

                VideoTransferProtocolKey waitSentKey = VideoTransferProtocolKey.VideoTransfer;
                lock (queueLocker)
                {
                    if (tcpSendQueue.Count > 0)
                    {
                        waitSentKey = tcpSendQueue.Dequeue();
                    }
                }

                if (waitSentKey == VideoTransferProtocolKey.VideoTransfer) continue;

                List<byte> sendBytes = new List<byte>(4);
                sendBytes.Add((byte)VideoTransferProtocolKey.Header1);
                sendBytes.Add((byte)VideoTransferProtocolKey.Header2);
                sendBytes.Add(clientDeviceIndex);
                sendBytes.Add((byte)waitSentKey);
                if (waitSentKey == VideoTransferProtocolKey.RSAKey)
                {

                    byte[] publicKeyBytes = Encoding.UTF8.GetBytes(publicKey);
                    sendBytes.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(publicKeyBytes.Length)));
                    sendBytes.AddRange(publicKeyBytes);
                }
                try
                {
                    tcpTransferSocket.Send(sendBytes.ToArray());
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionAborted || ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        EndAllLoop();
                        Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp transfer send datas failed.", ex);
                    }
                    else
                    {
                        Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "Not deal exception.", ex);
                        throw ex;
                    }
                }
                Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp transfer send cmd '" + waitSentKey.ToString() + "'.");
            }

            FinishAllConnection();

            ifTcpConnectionEstablished = false;
            Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client tcp transfer stops to send datas.");
        }

        /// <summary>
        /// 发送指令
        /// </summary>
        private void SendCmd(VideoTransferProtocolKey protocolKey)
        {
            lock (queueLocker)
            {
                tcpSendQueue.Enqueue(protocolKey);
            }
        }

        /// <summary>
        /// UDP接收数据任务
        /// </summary>
        /// <param name="cancelFlag">停止标志</param>
        private void UDPTransferRecieveTaskWork(CancellationToken cancelFlag)
        {
            while (true)
            {
                if (cancelFlag.IsCancellationRequested) break;

                // 接收收到的数据并处理
                byte[] recieveBuffer = new byte[maxVideoByteLength + 11];
                try
                {
                    udpTransferSocket.ReceiveFrom(recieveBuffer, ref serverEndPoint);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        EndAllLoop();
                        Logger.HistoryPrinting(Logger.Level.INFO, MethodBase.GetCurrentMethod().DeclaringType.FullName, "WinForm video client udp transfer recieve datas failed.", ex);
                        return;
                    }
                    else
                    {
                        Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "Not deal exception.", ex);
                        throw ex;
                    }
                }
                if (!((IPEndPoint)serverEndPoint).Address.Equals(IPAddress.Parse(ifAtSamePC ? serverIPAtSamePC : (ifAtSameLAN ? serverIPAtSameLAN : serverIPAtWAN)))) continue;
                UDPRecieveDatasDeal(recieveBuffer);
            }
        }

        /// <summary>
        /// 处理UDP传输socket接收到的数据
        /// </summary>
        /// <param name="datas">所收数据</param>
        private void UDPRecieveDatasDeal(byte[] datas)
        {
            if (datas[0] != (byte)VideoTransferProtocolKey.Header1 || datas[1] != (byte)VideoTransferProtocolKey.Header2)
            {
                return;
            }

            if (datas[2] != clientDeviceIndex || (VideoTransferProtocolKey)datas[3] != VideoTransferProtocolKey.VideoTransfer)
            {
                return;
            }

            if (udpTransferSocket.ReceiveTimeout > udpTransferSocketSendTimeOut) udpTransferSocket.ReceiveTimeout = udpTransferSocketSendTimeOut;

            int packDataLength = Convert.ToInt32(
                             IPAddress.NetworkToHostOrder(
                             BitConverter.ToInt32(datas, 4)));
            byte packIndex = datas[8];
            byte packCount = datas[9];
            byte packNum = datas[10];

            if (bufferReceivePackIndex == 0) // New data
            {
                if (packCount > 1) // Multiply packs
                {
                    bufferReceivePackIndex = packIndex;
                    bufferReceivePackNum.Add(packNum);
                    IEnumerable<byte> byteDatas = datas.Skip(11).Take(packDataLength - 3);
                    bufferReceivePackContent.Add(byteDatas.ToArray());
                }
                else // Single Pack
                {
                    lastReceivePackIndex = packIndex;
                    IEnumerable<byte> byteDatas = datas.Skip(11).Take(packDataLength - 3);
                    DecryptAndShowImg(byteDatas.ToArray());
                }
            }
            else // Old Data
            {
                if (packIndex == bufferReceivePackIndex) // Same Index
                {
                    bufferReceivePackNum.Add(packNum);
                    IEnumerable<byte> byteDatas = datas.Skip(11).Take(packDataLength - 3);
                    bufferReceivePackContent.Add(byteDatas.ToArray());

                    if (packNum == packCount) // Last pack
                    {
                        byte[] sortedIndex = new byte[packCount];
                        for (byte i = 0; i < packCount; ++i)
                        {
                            sortedIndex[bufferReceivePackNum[i] - 1] = i;
                        }
                        List<byte> byteTotalDatas = new List<byte>(131070);
                        for (byte k = 0; k < packCount; ++k)
                        {
                            byteTotalDatas.AddRange(bufferReceivePackContent[sortedIndex[k]]);
                        }
                        DecryptAndShowImg(byteTotalDatas.ToArray());

                        bufferReceivePackIndex = 0;
                        bufferReceivePackNum.Clear();
                        bufferReceivePackContent.Clear();
                    }
                }
                else // Different Index
                {
                    bufferReceivePackIndex = 0;
                    bufferReceivePackNum.Clear();
                    bufferReceivePackContent.Clear();

                    if (packCount > 1) // Multiply packs
                    {
                        bufferReceivePackIndex = packIndex;
                        bufferReceivePackNum.Add(packNum);
                        IEnumerable<byte> byteDatas = datas.Skip(11).Take(packDataLength - 3);
                        bufferReceivePackContent.Add(byteDatas.ToArray());
                    }
                    else // Single Pack
                    {
                        lastReceivePackIndex = packIndex;
                        IEnumerable<byte> byteDatas = datas.Skip(11).Take(packDataLength - 3);
                        DecryptAndShowImg(byteDatas.ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// 解密并显示图像
        /// </summary>
        /// <param name="encryptedBytes">加密数据</param>
        private void DecryptAndShowImg(byte[] encryptedBytes)
        {
            int byteLength = encryptedBytes.Length;
            int unitLength = keyLength / 8;
            if (byteLength % unitLength != 0) return;
            int segmentNum = byteLength / unitLength;
            List<byte> decryptedBytesList = new List<byte>(byteLength);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(privateKey);
                for (int i = 0; i < segmentNum; ++i)
                {
                    IEnumerable<byte> buffer = encryptedBytes.Skip(i * unitLength).Take(unitLength);
                    decryptedBytesList.AddRange(rsa.Decrypt(buffer.ToArray(), false));
                }
            }

            using (MemoryStream ms = new MemoryStream(decryptedBytesList.ToArray()))
            {
                IBShow.Image = new Image<Bgr, byte>((Bitmap)Image.FromStream(ms)); // Bitmap->Image
            }
        }


        private string remoteDevicePublicKey = null;
        private const int remoteDevicePublicKeyLength = 1024;
        Capture camera = new Capture(0);
        List<long> time1 = new List<long>(), time2 = new List<long>(), time3 = new List<long>();
        List<long> len1 = new List<long>(), len2 = new List<long>(), len3 = new List<long>();
        /// <summary>
        /// TCP传输心跳定时器
        /// </summary>
        private void tcpSendClocker_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //SendCmd(VideoTransferProtocolKey.PingSignal);

            remoteDevicePublicKey = publicKey;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            // 得到图像
            Mat pic = new Mat();
            camera.Retrieve(pic, 0);

            // 得到图像压缩后的字节流
            byte[] imgBytes;
            Bitmap ImgBitmap = pic.ToImage<Bgr, byte>().Bitmap;
            using (MemoryStream ms = new MemoryStream())
            {
                ImgBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                imgBytes = ms.GetBuffer();
            }
            sw.Stop();
            time1.Add(sw.ElapsedMilliseconds);
            len1.Add(imgBytes.Length);
            /*
            sw.Restart();
            // 利用公钥加密
            int byteLength = imgBytes.Length;
            int unitLength = remoteDevicePublicKeyLength / 8 - 11;
            int intgePart = byteLength / unitLength;
            int segmentNum = intgePart + 1;
            int totalLength = segmentNum * (remoteDevicePublicKeyLength / 8);
            List<byte> sendBytesList = new List<byte>(totalLength);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(remoteDevicePublicKey);
                for (int i = 0; i < segmentNum - 1; ++i)
                {
                    IEnumerable<byte> buffer = imgBytes.Skip(i * unitLength).Take(unitLength);
                    sendBytesList.AddRange(rsa.Encrypt(buffer.ToArray(), false));
                }
                IEnumerable<byte> finalBuffer = imgBytes.Skip((segmentNum - 1) * unitLength);
                sendBytesList.AddRange(rsa.Encrypt(finalBuffer.ToArray(), false));
            }

            sw.Stop();
            time2.Add(sw.ElapsedMilliseconds);

            sw.Restart();
            byte[] encryptedBytes = sendBytesList.ToArray();

            int byteLength2 = encryptedBytes.Length;
            int unitLength2 = keyLength / 8;

            int segmentNum2 = byteLength2 / unitLength2;
            List<byte> decryptedBytesList = new List<byte>(byteLength2);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(privateKey);
                for (int i = 0; i < segmentNum2; ++i)
                {
                    IEnumerable<byte> buffer = encryptedBytes.Skip(i * unitLength2).Take(unitLength2);
                    decryptedBytesList.AddRange(rsa.Decrypt(buffer.ToArray(), false));
                }
            }

            sw.Stop();
            time3.Add(sw.ElapsedMilliseconds);
            */
            using (MemoryStream ms = new MemoryStream(imgBytes))
            {
                imageBox1.Image = new Image<Bgr, byte>((Bitmap)Image.FromStream(ms)); // Bitmap->Image
            }

            sw.Restart();
            // 利用公钥加密
            byte[] encryptedBytes = EncryptByAES(imgBytes);
            sw.Stop();
            time2.Add(sw.ElapsedMilliseconds);
            len2.Add(encryptedBytes.Length);

            sw.Restart();
            byte[] decryptedBytes = DecryptByAES(encryptedBytes);
            sw.Stop();
            time3.Add(sw.ElapsedMilliseconds);
            len3.Add(decryptedBytes.Length);

            using (MemoryStream ms = new MemoryStream(decryptedBytes))
            {
                IBShow.Image = new Image<Bgr, byte>((Bitmap)Image.FromStream(ms)); // Bitmap->Image
            }

            tcpSendClocker.Start();
        }

        private byte[] commonKey = null;
        private byte[] commonIV = null;
        /// <summary>
        /// AES加密数据
        /// </summary>
        /// <param name="nonEncryptedBytes">待加密字节流</param>
        /// <returns>加密后的字节流</returns>
        private byte[] EncryptByAES(byte[] nonEncryptedBytes)
        {
            if (Object.Equals(nonEncryptedBytes, null) || nonEncryptedBytes.Length < 1)
            {
                Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "Datas for encrypting by AES is abnormal.");
                return null; // 待加密数据异常
            }
            if (Object.Equals(commonIV, null) ||
                Object.Equals(commonKey, null))
            {
                Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "AES key has not been known yet.");
                return null; // AES密钥和初始向量未知
            }

            string nonEncryptedString = Convert.ToBase64String(nonEncryptedBytes);

            byte[] encryptedBytes = null;
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.Key = commonKey; aes.IV = commonIV;
                ICryptoTransform encryptorByAES = aes.CreateEncryptor();

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptorByAES, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(nonEncryptedString);
                        }
                        encryptedBytes = msEncrypt.ToArray();
                    }
                }
            }

            return encryptedBytes;
        }

        /// <summary>
        /// AES解密数据
        /// </summary>
        /// <param name="encryptedBytes">待解密字节流</param>
        /// <returns>解密后的字节流</returns>
        private byte[] DecryptByAES(byte[] encryptedBytes)
        {
            if (Object.Equals(encryptedBytes, null) || encryptedBytes.Length < 1)
            {
                Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "Datas for decrypting by AES is abnormal.");
                return null; // 待解密数据异常
            }
            if (Object.Equals(commonIV, null) ||
                Object.Equals(commonKey, null))
            {
                Logger.HistoryPrinting(Logger.Level.WARN, MethodBase.GetCurrentMethod().DeclaringType.FullName, "AES key has not been known yet.");
                return null; // AES密钥和初始向量未知
            }

            byte[] decryptedBytes = null;
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.Key = commonKey; aes.IV = commonIV;
                ICryptoTransform decryptorByAES = aes.CreateDecryptor();

                using (MemoryStream msDecrypt = new MemoryStream(encryptedBytes))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptorByAES, CryptoStreamMode.Read))
                    {
                        using (StreamReader swDecrypt = new StreamReader(csDecrypt))
                        {
                            string decryptedString = swDecrypt.ReadToEnd();
                            decryptedBytes = Convert.FromBase64String(decryptedString);
                        }
                    }
                }
            }
            return decryptedBytes;
        }

        /// <summary>
        /// 结束所有循环等待
        /// </summary>
        private void EndAllLoop()
        {
            tcpTransferCancel.Cancel();
            tcpSendClocker.Stop();
            udpTransferCancel.Cancel();
        }

        /// <summary>
        /// 结束所有连接
        /// </summary>
        private void FinishAllConnection()
        {
            tcpTransferSocket.Shutdown(SocketShutdown.Both);
            tcpTransferSocket.Close();

            udpTransferSocket.Shutdown(SocketShutdown.Both);
            udpTransferSocket.Close();
        }

    }
}
