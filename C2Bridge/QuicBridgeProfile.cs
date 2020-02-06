using System;
using System.Net;
using System.Threading;
using System.Text;
using QuicNet;
using QuicNet.Streams;
using QuicNet.Connections;

public interface IMessenger
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
}