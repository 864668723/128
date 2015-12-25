using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer
{
    class Server : FileTransferForm
    {
        static public int UdpPort = 13010;

        static LinkedList<IPEndPoint> clientIPEndPoints = new LinkedList<IPEndPoint>();
        static LinkedList<TcpClient> tcpClients = new LinkedList<TcpClient>();
        static LinkedList<IPAddress> clientIPAdresses = new LinkedList<IPAddress>();

        public Server()
        {
            Text = "Server";
            label1.Text = "0 Client(s) Online";

            int[] array = new int[100];
            array[0] = 11010;
            for(int i = 1; i < array.Length; i++)
            {
                array[i] = array[0] + i;
            }
            availablePorts = new LinkedList<int>(array);

            udpReceiveAsync(UdpPort, new HandleMessage(async (message, ip) => {
                if (message == "Client Online" && !isConnected(ip))
                {
                    clientIPAdresses.AddLast(ip);
                    await replyUdpAsync("Server Online", ip, Client.UdpPort);
                    await replyUdpAsync("Request Port", ip, Client.UdpPort);
                } else if (message == "Request Port")
                {
                    int port = getPort();
                    tcpReceiveAsync(port);
                    await replyUdpAsync(port.ToString(), ip, Client.UdpPort);
                } else if (message != "Client Online")
                {
                    int port = Int32.Parse(message);
                    IPEndPoint ipe = new IPEndPoint(ip, port);
                    clientIPEndPoints.AddLast(ipe);
                    TcpClient tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(ip, port);
                    tcpClients.AddLast(tcpClient);

                    label1.Text = clientIPEndPoints.Count().ToString() + " Client(s) Online";

                    if (checkBox1.Checked)
                    {
                        await Task.Run(async () => {
                            await sendFilesAsync(tcpClient);
                        });
                    }
                }
            }));
            broadcast("Server Online", Client.UdpPort);
        }

        protected override async void button1_Click(object sender, EventArgs e)
        {
            await sendFilesAsync(tcpClients);
        }

        protected override void tcpClosed(IPAddress ip, int port)
        {
            for(LinkedListNode<IPEndPoint> it = clientIPEndPoints.First; it != null; it = it.Next)
            {
                if(it.Value.Address.ToString() == ip.ToString())
                {
                    clientIPEndPoints.Remove(it);
                    break;
                }
            }

            for(LinkedListNode<TcpClient> it = tcpClients.First; it != null; it = it.Next)
            {
                IPEndPoint iep = (IPEndPoint)it.Value.Client.RemoteEndPoint;
                if(iep.Address.ToString() == ip.ToString())
                {
                    tcpClients.Remove(it);
                    break;
                }
            }

            for (LinkedListNode<IPAddress> it = clientIPAdresses.First; it != null; it = it.Next)
            {
                if (it.Value.ToString() == ip.ToString())
                {
                    clientIPAdresses.Remove(it);
                    break;
                }
            }

            recyclePort(port);

            label1.Text = clientIPEndPoints.Count().ToString() + " Client(s) Online";
        }

        protected bool isConnected(IPAddress ip)
        {
            foreach (IPAddress ip2 in clientIPAdresses)
            {
                if (ip2.ToString() == ip.ToString())
                    return true;
            }

            return false;
        }
    }
}
