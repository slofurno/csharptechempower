using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

#if __MonoCS__
using Mono.Data.Sqlite;
#else
using System.Data.SQLite;
#endif

using Jil;
using Dapper;
using System.IO;
using System.Net.Sockets;

namespace httplistener
{
  class Program
  {

    static string RESPONSE = "HTTP/1.1 200 OK\r\nContent-Length: {0}\r\nContent-Type: {1}; charset=UTF-8\r\nServer: Example\r\nDate: Wed, 17 Apr 2013 12:00:00 GMT\r\n\r\n{2}";

    static string CACHED = null;

    static void Main(string[] args)
    {

      System.Net.ServicePointManager.DefaultConnectionLimit = int.MaxValue;
      System.Net.ServicePointManager.UseNagleAlgorithm = false;
      SqliteContext.datasource = "fortunes.sqlite";
      Listen().Wait();

    }

    static async Task Listen()
    {

      var server = new TcpListener(IPAddress.Any,8080);
      server.Start();


      while (true)
      {
       // var context = await listener.GetContextAsync().ConfigureAwait(false);
        TcpClient client = await server.AcceptTcpClientAsync();
        Task.Run(()=>Serve(client));

      }
    }

    static async Task Serve(TcpClient client)
    {

      byte del = (byte)'\r';
      var header = new byte[1024];
      int read;
      int hlen = -1;

      using (var rw = client.GetStream())
      {

        while (hlen == -1 && (read = rw.Read(header, 0, 1024)) > 0)
        {
          for (var i = 0; i < read; i++)
          {
            if (header[i] == del)
            {
              hlen = i;
              break;
            }
          }
        }

        var get = Encoding.UTF8.GetString(header, 0, hlen);
        var url = get.Split(' ')[1].Split('?');

        using (var writer = new StreamWriter(rw))
        {
          switch (url[0])
          {
            case "/plaintext":
              //await Plaintext(writer).ConfigureAwait(false);
              break;
            case "/json":
              await Json(writer);
              break;
            case "/db":
              //await Db(writer).ConfigureAwait(false);
              break;
            case "/fortunes":
              await Fortunes(writer);
              break;
            case "/queries":
              await Queries(writer, url);
              break;
            default:
              //await NotFound(writer).ConfigureAwait(false);
              break;
          }
        }

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
      var body =  "Hello, World!";
      await response.WriteAsync(string.Format(RESPONSE, body.Length, "text/plain", body));
      await response.FlushAsync();

    }

    private static Task Json(StreamWriter response)
    {
      var json = JSON.SerializeDynamic(new { message = "Hello, World!" });

      return response.WriteAsync(string.Format(RESPONSE, json.Length, "application/json", json));
      //response.Flush();
    }

    private static Task Queries(StreamWriter response, string[] url)
    {

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
      return response.WriteAsync(string.Format(RESPONSE, json.Length, "application/json", json));

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

    
    private static Task Fortunes(StreamWriter response)
    {
      List<Fortune> fortunes;

      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();
        fortunes = conn.Query<Fortune>(@"SELECT * FROM Fortune").ToList();
        conn.Close();
      }

      fortunes.Add(new Fortune { ID = 0, Message = "Additional fortune added at request time." });
      fortunes.Sort();

      const string header = "<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>";

      const string footer = "</table></body></html>";

      var body = string.Join("", fortunes.Select(x => "<tr><td>" + x.ID + "</td><td>" + x.Message + "</td></tr>"));

      var content =  header + body + footer;
      return response.WriteAsync(string.Format(RESPONSE, content.Length, "text/html", content));

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

#if __MonoCS__
    public static SqliteConnection conn = new SqliteConnection("Data Source=" + datasource + ";Version=3;Pooling=True;Max Pool Size=20");
#else

    public static SQLiteConnection conn = new SQLiteConnection("Data Source=" + datasource + ";Version=3;Pooling=True;Max Pool Size=20");
#endif

    public static string datasource;
#if __MonoCS__
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
