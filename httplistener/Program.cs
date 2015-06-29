using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;



#if _LINUX_
using Mono.Data.Sqlite;
#else
using System.Data.SQLite;
#endif

using Jil;
using Dapper;
using System.IO;
using System.Net.Sockets;
using System.Threading;

using System.Collections.Concurrent;

namespace httplistener
{
  class Program
  {

    static string RESPONSE = "HTTP/1.1 200 OK\r\nContent-Length: {0}\r\nContent-Type: {1}; charset=UTF-8\r\nServer: Example\r\nDate: Wed, 17 Apr 2013 12:00:00 GMT\r\n\r\n{2}";

    static Stack<SocketAsyncEventArgs> availableConnections;
    static SliceManager sliceManager = new SliceManager(4096, 12000);
    static AutoResetEvent _listenNext = new AutoResetEvent(true);
    static int _currentOpenSockets = 0;
    static BlockingCollection<SocketAsyncEventArgs> _connections;

    static void Main(string[] args)
    {
      var dontquit = new AutoResetEvent(false);

      Init();
      System.Net.ServicePointManager.DefaultConnectionLimit = int.MaxValue;
      System.Net.ServicePointManager.UseNagleAlgorithm = false;
      //var qqq = ThreadPool.SetMinThreads(1, 4);
      Task.Run(() =>
      {
        while (true)
        {
          var next = _connections.Take();
          ProcessReceive(next);
        }

      });
      Listen();
      dontquit.WaitOne();

    }

    static Socket listenSocket;
    static SocketAsyncEventArgs acceptEventArg;

    static void Init()
    {
      availableConnections = new Stack<SocketAsyncEventArgs>();

      for (int i = 0; i < 12000; i++)
      {
        var next = new SocketAsyncEventArgs();
        next.Completed += new EventHandler<SocketAsyncEventArgs>(SocketEventComplete);
        next.UserToken = new UserSocket();
        sliceManager.setBuffer(next);

        availableConnections.Push(next);
      }


      var endpoint = new IPEndPoint(IPAddress.Any, 8080);
      listenSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
      listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
      listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

      listenSocket.Bind(endpoint);
      listenSocket.Listen(4000);

      acceptEventArg = new SocketAsyncEventArgs();
      acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(ProcessAccept);

      _connections = new BlockingCollection<SocketAsyncEventArgs>();
      


    }

    static void SocketEventComplete(object sender, SocketAsyncEventArgs e)
    {
      switch (e.LastOperation)
      {
        case SocketAsyncOperation.Receive:

          _connections.Add(e);

          //ProcessReceive(e);
          break;
        case SocketAsyncOperation.Send:
          ProcessSend(e);
          break;
        default:
          throw new ArgumentException("The last operation completed on the socket was not a receive or send");
      }
    }

    static void ProcessReceive(SocketAsyncEventArgs e)
    {

      int read = e.BytesTransferred;

      UserSocket token = (UserSocket)e.UserToken;
      if (read > 0 && e.SocketError == SocketError.Success)
      {
        var space = new int[2];
        int s = 0;
        const byte del = (byte)'\r';
        const byte sep = (byte)0x20;
        var buffer = e.Buffer;
        int offset = e.Offset;

        int hlen = -1;


        for (var i = 0; i < read; i++)
        {
          switch (buffer[offset + i])
          {
            case sep:
              space[s] = offset + i;
              s++;
              break;
            case del:
              hlen = offset + i;
              //zz
              i = read;
              break;
          }

        }

        var len = space[1] - space[0] - 1;

        if (len > 0)
        {
          var path = Encoding.UTF8.GetString(buffer, space[0] + 1, space[1] - space[0] - 1);

          Serve(e, path);
        }
        else
        {
          Console.WriteLine("read " + read + " bytes...");
          CloseClientSocket(e);
        }

      }
      else
      {
        CloseClientSocket(e);
      }
    }

    static void ProcessSend(SocketAsyncEventArgs e)
    {

      CloseClientSocket(e);

      /*
      if (e.SocketError == SocketError.Success)
      {
        // done echoing data back to the client
        UserSocket token = (UserSocket)e.UserToken;
        // read the next block of data send from the client 
        bool willRaiseEvent = token.Socket.ReceiveAsync(e);
        if (!willRaiseEvent)
        {
          ProcessReceive(e);
        }
      }
      else
      {
        CloseClientSocket(e);
      }
       * */
    }


    static void CloseClientSocket(SocketAsyncEventArgs e)
    {
      UserSocket token = e.UserToken as UserSocket;

      // close the socket associated with the client 
      try
      {
        token.Socket.Shutdown(SocketShutdown.Send);
      }
      // throws if client process has already closed 
      catch (Exception) { }
      token.Socket.Close();
      e.UserToken = null;
      e.SetBuffer(e.Offset, 4096);
      // Free the SocketAsyncEventArg so they can be reused by another client
      lock (availableConnections)
      {
        availableConnections.Push(e);
        _currentOpenSockets--;

      }
    }

    static void ProcessAccept(Object sender, SocketAsyncEventArgs e)
    {

      SocketAsyncEventArgs connection;
      Socket socket = e.AcceptSocket;
      _listenNext.Set();

      lock (availableConnections)
      {
        connection = availableConnections.Pop();
        _currentOpenSockets++;
      }

      connection.UserToken = new UserSocket(socket);
      if (!socket.ReceiveAsync(connection))
      {

        _connections.Add(connection);

        /*
        Task.Run(() =>
        {
          ProcessReceive(connection);
        });
         * */
      }

    }

    static void Listen()
    {

      while (true)
      {
        _listenNext.WaitOne();
        acceptEventArg.AcceptSocket = null;

        if (!listenSocket.AcceptAsync(acceptEventArg))
        {
          ProcessAccept(null, acceptEventArg);
        }
        else
        {
          Console.WriteLine("deferred");
        }
      }

    }


    static void Serve(SocketAsyncEventArgs e, string path)
    {

      String response;

      switch (path)
      {
        case "/plaintext":
          response = "";
          //await Plaintext(writer).ConfigureAwait(false);
          break;
        case "/json":
          response = Json();
          break;
        case "/db":
          response = "";
          //await Db(writer).ConfigureAwait(false);
          break;
        case "/fortunes":
          response = Fortunes();
          break;
        case "/queries":
          response = Queries(path);
          break;
        default:
          response = "";
          //await NotFound(writer).ConfigureAwait(false);
          break;
      }

      var token = (UserSocket)e.UserToken;
      //Encoding.UTF8.get

      var len = Encoding.UTF8.GetBytes(response, 0, response.Length, e.Buffer, e.Offset);
      e.SetBuffer(e.Offset, len);

      if (!token.Socket.SendAsync(e))
      {
        ProcessSend(e);
      }


    }


    public static int writeBinary(string src, byte[] dst)
    {
      var len = src.Length;
      for (int i = 0; i < len; i++)
      {
        dst[i] = (byte)src[i];
      }
      return len;
    }



    private static async Task NotFound(StreamWriter response)
    {
      var body = "Not Found!";
      response.Write(string.Format(RESPONSE, body.Length, "text/plain", body));
      response.Flush();

    }

    private static async Task Plaintext(StreamWriter response)
    {
      var body = "Hello, World!";
      await response.WriteAsync(string.Format(RESPONSE, body.Length, "text/plain", body));
      await response.FlushAsync();

    }

    private static string Json()
    {
      var json = JSON.SerializeDynamic(new { message = "Hello, World!" });

      return string.Format(RESPONSE, json.Length, "application/json", json);
      //response.Flush();
    }

    private static string Queries(string path)
    {
      string[] url = path.Split('?');

      string raw;
      int count = 1;

      if (url.Length > 1)
      {
        var qs = parseQuery(url[1]);
        if (qs.TryGetValue("queries", out raw))
        {
          if (int.TryParse(raw, out count))
          {
            count = Math.Min(500, count);
          }
        }
      }

      var results = new RandomNumber[count];

      var rnd = new Random();
      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();
        for (var i = 0; i < count; i++)
        {
          var id = rnd.Next(10000) + 1;
          var randomNumber = conn.Query<RandomNumber>(@"SELECT * FROM World WHERE id=@id", new { id = id }).FirstOrDefault();
          results[i] = randomNumber;
        }
      }

      var json = JSON.Serialize<RandomNumber[]>(results);
      return string.Format(RESPONSE, json.Length, "application/json", json);

    }

    static Dictionary<string, string> parseQuery(string queryString)
    {
      var r = new Dictionary<string, string>();

      if (queryString == null)
      {
        return r;
      }

      var s = queryString.Split('&');

      foreach (var p in s)
      {
        var kvp = p.Split('=');
        r[kvp[0]] = kvp[1];
      }

      return r;
    }

    public static int GetQueries(HttpListenerRequest request)
    {
      int queries = 1;
      string queriesString = request.QueryString["queries"];
      if (queriesString != null)
      {
        // If this fails to parse, queries will be set to zero.
        int.TryParse(queriesString, out queries);
        queries = Math.Max(1, Math.Min(500, queries));
      }
      return queries;
    }

    private static async Task Db(StreamWriter response)
    {

      var rnd = new Random();
      var id = rnd.Next(10000);
      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();

        var result = conn.Query<RandomNumber>(@"SELECT * FROM World WHERE id=@id", new { id = id }).FirstOrDefault();

        var json = JSON.Serialize<RandomNumber>(result);

        await response.WriteAsync(string.Format(RESPONSE, json.Length, "application/json", json));
      }
    }


    private static string Fortunes()
    {
      List<Fortune> fortunes;
      var conn = SqliteContext.GetConnection();

      fortunes = conn.Query<Fortune>(@"SELECT Id,Message FROM Fortune").ToList();

      fortunes.Add(new Fortune { ID = 0, Message = "Additional fortune added at request time." });
      fortunes.Sort();

      const string header = "<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>";

      const string footer = "</table></body></html>";

      var body = string.Join("", fortunes.Select(x => "<tr><td>" + x.ID + "</td><td>" + x.Message + "</td></tr>"));
      var content = header + body + footer;
      var len = Encoding.UTF8.GetByteCount(content);

      return string.Format(RESPONSE, len, "text/html", content);
    }

  }


  public class RandomNumber
  {
    public int id { get; set; }
    public int randomNumber { get; set; }
  }

  public class Fortune : IComparable<Fortune>
  {
    public int ID { get; set; }
    public string Message { get; set; }

    public int CompareTo(Fortune other)
    {
      return Message.CompareTo(other.Message);
    }
  }

  public static class SqliteContext
  {



#if _LINUX_
    public static SqliteConnection conn;

     static SqliteContext()
    {
     conn = new SqliteConnection("Data Source=fortunes.sqlite;Version=3;Pooling=True;Max Pool Size=10;");
     conn.Open();
    }

#else

    private static SQLiteConnection conn;
    static SqliteContext()
    {
     // sem = new SemaphoreSlim(1);
      conn = new SQLiteConnection("Data Source=fortunes.sqlite;Version=3;Pooling=True;Max Pool Size=10;");
      conn.Open();
    }
#endif

    public static string datasource;
#if _LINUX_
    public static SqliteConnection GetConnection()
    {
      return conn;
    }
#else
    public static SQLiteConnection GetConnection()
    {
      return conn;
      
    }

#endif
    
  }

}
