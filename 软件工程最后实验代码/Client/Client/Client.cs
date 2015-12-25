using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer
{
    class Client : FileTransferForm
    {
        static public int UdpPort = 15010;

        static IPEndPoint serverIPEndPoint = null;
        static TcpClient tcpClient = null;
        static IPAddress serverIPAddress = null;

        public Client()
        {
            Text = "Client";
            label1.Text = "Server Offline";
            availablePorts = new LinkedList<int>(
                new int[] { 10050, 10051, 10052, 10053, 10054, 10055, 10056, 10057, 10058, 10059 });

            udpReceiveAsync(UdpPort, new HandleMessage(async (message, ip) => {
                if (message == "Server Online" && (serverIPAddress == null || serverIPAddress.ToString() != ip.ToString()))
                {
                    serverIPAddress = ip;
                    await replyUdpAsync("Client Online", ip, Server.UdpPort);
                    await replyUdpAsync("Request Port", ip, Server.UdpPort);
                } else if (message == "Request Port")
                {
                    int port = getPort();
                    tcpReceiveAsync(port);
                    await replyUdpAsync(port.ToString(), ip, Server.UdpPort);
                } else if(message != "Server Online")
                {
                    int port = Int32.Parse(message);
                    serverIPEndPoint = new IPEndPoint(ip, port);
                    tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(ip, port);

                    label1.Text = "Server Online";

                    if (checkBox1.Checked)
                    {
                        await Task.Run(async () => {
                            await sendFilesAsync(tcpClient);
                        });
                    }
                }
            }));
            broadcast("Client Online", Server.UdpPort);
        }

        protected override async void button1_Click(object sender, EventArgs e)
        {
            if (serverIPEndPoint == null || tcpClient == null) return;

            await sendFilesAsync(tcpClient);
        }

        protected override void tcpClosed(IPAddress ip, int port)
        {
            serverIPEndPoint = null;
            tcpClient = null;
            serverIPAddress = null;
            recyclePort(port);

            label1.Text = "Server Offline";
        }
    }
}
