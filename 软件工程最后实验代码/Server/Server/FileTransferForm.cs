using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer
{
    public abstract partial class FileTransferForm : System.Windows.Forms.Form
    {
        static String defaultPath = "D:\\Test\\";
        static protected LinkedList<int> availablePorts = new LinkedList<int>();
        static protected LinkedList<int> usingPorts = new LinkedList<int>();

        String[] fileNames = new String[0];
        int count = 0;//count how many data parts have sent

        public FileTransferForm()
        {
            InitializeComponent();

            openFileDialog1.Multiselect = true;
            button1.Enabled = false;

            progressBar1.Minimum = 0;
            progressBar1.Step = 1;
            progressBar1.Value = 0;
        }

        protected async Task broadcast(String message, int port)
        {
            UdpClient udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            udpClient.MulticastLoopback = true;
            udpClient.ExclusiveAddressUse = false;

            IPEndPoint ip = new IPEndPoint(IPAddress.Broadcast, port);
            byte[] bytes = Encoding.ASCII.GetBytes(message);
            await udpClient.SendAsync(bytes, bytes.Length, ip);
            udpClient.Close();
        }

        protected async void udpReceiveAsync(int port, HandleMessage handleMessage)
        {
            IPEndPoint remoteIP = new IPEndPoint(IPAddress.Any, port);
            UdpClient udpClient = new UdpClient(remoteIP);

            while (true)
            {
                UdpReceiveResult rst = await udpClient.ReceiveAsync();
                handleMessage(Encoding.ASCII.GetString(rst.Buffer), rst.RemoteEndPoint.Address);
            }
        }

        protected async Task replyUdpAsync(String replyMessage, IPAddress ip, int port)
        {
            UdpClient udpClient = new UdpClient();
            udpClient.MulticastLoopback = true;
            udpClient.ExclusiveAddressUse = false;
            
            byte[] bytes = Encoding.ASCII.GetBytes(replyMessage);
            await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(ip, port));
            udpClient.Close();
        }

        public delegate void HandleMessage(String message, IPAddress ip);

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            button1.Enabled = true;
        }

        protected abstract void button1_Click(object sender, EventArgs e);//Send

        private void button2_Click(object sender, EventArgs e)//Browse
        {
            DialogResult rst = openFileDialog1.ShowDialog();
            if (rst == DialogResult.OK)
            {
                fileNames = openFileDialog1.FileNames;

                StringBuilder sb = new StringBuilder();
                sb.Append(new FileInfo(fileNames.First()).DirectoryName + "\\");
                foreach(String fileName in fileNames)
                {
                    FileInfo fileInfo = new FileInfo(fileName);
                    sb.Append(fileInfo.Name);
                    sb.Append(" | ");
                }

                textBox1.Text = sb.Remove(sb.Length - 3, 3).ToString();
            }
        }

        protected async Task sendFilesAsync(LinkedList<TcpClient> tcpClients)
        {
            setProgressBar(tcpClients.Count);

            foreach(TcpClient tcpClient in tcpClients) {
                await sendFilesAsync_(tcpClient);
            }

            Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = 0;
            });

            count = 0;
        }

        protected async Task sendFilesAsync(TcpClient tcpClient)
        {
            setProgressBar(1);
            await sendFilesAsync_(tcpClient);
            Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = 0;
            });
            count = 0;
        }

        private async Task sendFilesAsync_(TcpClient tcpClient)
        {
            NetworkStream networkStream = tcpClient.GetStream();

            foreach (String fileName in fileNames)
            {
                //send FileInfo
                FileInfo fileInfo = new FileInfo(fileName);
                byte[] bytes1 = Encoding.ASCII.GetBytes(fileInfo.Name);
                byte[] bytes2 = BitConverter.GetBytes(fileInfo.Length);

                byte[] buffer = new byte[1024];
                Array.Copy(bytes1, buffer, bytes1.Length);
                Array.Copy(bytes2, 0, buffer, 256, 8);

                int numRead = 256 + 8;
                FileStream fileStream = File.OpenRead(fileName);

                do
                {
                    await networkStream.WriteAsync(buffer, 0, numRead);
                    count++;

                    Invoke((MethodInvoker)delegate
                    {
                        progressBar1.PerformStep();
                    });
                } while ((numRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) != 0);

                fileStream.Close();
            }

            await networkStream.FlushAsync();
        }

        protected async void tcpReceiveAsync(int port)
        {
            IPEndPoint iep = new IPEndPoint(IPAddress.Any, port);
            TcpListener tcpListener = new TcpListener(iep);
            tcpListener.Start();

            TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
            byte[] buffer = new byte[1024];
            NetworkStream networkStream = tcpClient.GetStream();

            while (true)
            {
                try {
                    //get fileName & fileSize
                    byte[] bytes1 = new byte[256];
                    if (await networkStream.ReadAsync(bytes1, 0, 256) < 256) continue;
                    string fileName = Encoding.ASCII.GetString(bytes1);
                    int index = fileName.IndexOf('\0');
                    fileName = fileName.Substring(0, index);

                    byte[] bytes2 = new byte[8];
                    if (await networkStream.ReadAsync(bytes2, 0, 8) < 8) continue;
                    long fileSize = BitConverter.ToInt64(bytes2, 0);

                    int numRead = 0;
                    FileStream fileStream = File.OpenWrite(defaultPath + fileName);

                    while (true)
                    {
                        if (fileSize <= buffer.Length)
                        {
                            if (fileSize > 0)
                            {
                                numRead = await networkStream.ReadAsync(buffer, 0, (int)fileSize);
                                fileStream.Write(buffer, 0, numRead);
                                fileSize -= numRead;
                            }
                            break;
                        }
                        else if ((numRead = await networkStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            fileStream.Write(buffer, 0, numRead);
                            fileSize -= numRead;
                        }
                        else
                        {
                            break;
                        }
                    }

                    await fileStream.FlushAsync();
                    fileStream.Close();
                } catch(Exception e)
                {
                    Debug.WriteLine(e.ToString());
                    tcpClosed(((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address, port);
                    return;
                }
            }
        }

        private void setProgressBar(int count)
        {
            long totalSize = 0;
            foreach (String fileName in fileNames)
            {
                totalSize += new FileInfo(fileName).Length;
            }

            Invoke((MethodInvoker)delegate
            {
                progressBar1.Maximum = (int)((totalSize + (256 + 8) * fileNames.Count()) / 1024 * count) + 1;
                progressBar1.Value = 0;
            });
        }


        protected int getPort()
        {
            int r = availablePorts.First();
            availablePorts.RemoveFirst();
            usingPorts.AddLast(r);
            return r;
        }

        protected void recyclePort(int port)
        {
            usingPorts.Remove(port);
            availablePorts.AddLast(port);
        }

        protected abstract void tcpClosed(IPAddress ip, int port);
    }
}
