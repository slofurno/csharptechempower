using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

using Mono.Data.Sqlite;

using Jil;
using Dapper;
using System.IO;
using System.Net.Sockets;

namespace httplistener
{
  class Program
  {

    static string RESPONSE = "HTTP/1.1 200 OK\r\nContent-Length: {0}\r\nContent-Type: {1}; charset=UTF-8\r\nServer: Example\r\nDate: Wed, 17 Apr 2013 12:00:00 GMT\r\n\r\n{2}";

    static void Main(string[] args)
    {

      Process Proc = Process.GetCurrentProcess();
      Proc.ProcessorAffinity = (IntPtr)3;
      System.Net.ServicePointManager.DefaultConnectionLimit = int.MaxValue;
      System.Net.ServicePointManager.UseNagleAlgorithm = false;
      SqliteContext.datasource = "fortunes.sqlite";
      Listen().Wait();

    }

    static async Task Listen()
    {

      var server = new TcpListener(IPAddress.Parse("127.0.0.1"),8080);
      server.Start();


      while (true)
      {
       // var context = await listener.GetContextAsync().ConfigureAwait(false);
        var client = await server.AcceptTcpClientAsync();
        Serve(client.GetStream());

      }
    }

    static async Task Serve(Stream rw)
    {

      byte del = (byte)'\r';
      var header = new byte[1024];
      int read;
      int hlen = -1;


      while (hlen == -1 && (read = await rw.ReadAsync(header, 0, 1024))>0)
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

      string responseString = null;
      using (var writer = new StreamWriter(rw))
      {
        writer.AutoFlush = true;
        switch (url[0])
        {
          case "/plaintext":
            await Plaintext(writer);
            break;
          case "/json":
            await Json(writer);
            break;
          case "/db":
            await Db(writer);
            break;
          case "/fortunes":
            await Fortunes(writer);
            break;
          case "/queries":
            await Queries(writer, url[1]);
            break;
          default:
            await NotFound(writer);
            break;
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
      await response.WriteAsync(string.Format(RESPONSE, body.Length, "text/plain", body));
      await response.FlushAsync();

    }

    private static async Task Plaintext(StreamWriter response)
    {
      var body =  "Hello, World!";
      await response.WriteAsync(string.Format(RESPONSE, body.Length, "text/plain", body));
      await response.FlushAsync();

    }

    private static async Task Json(StreamWriter response)
    {
      var json = JSON.SerializeDynamic(new { message = "Hello, World!" });

      await response.WriteAsync(string.Format(RESPONSE, json.Length, "application/json", json));
      await response.FlushAsync();

    }

    private static async Task Queries(StreamWriter response, string queryString)
    {

      var qs = parseQuery(queryString);
      string raw;
      int count = 1;

      if (qs.TryGetValue("queries", out raw))
      {
        if (int.TryParse(raw, out count))
        {
          count = Math.Min(500, count);
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
      Console.WriteLine(json);


      await response.WriteAsync(string.Format(RESPONSE, json.Length, "application/json", json));
      await response.FlushAsync();

    }

    static Dictionary<string, string> parseQuery(string qs)
    {
      var s = qs.Split('&');
      var r = new Dictionary<string, string>();

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

    
    private static async Task Fortunes(StreamWriter response)
    {
      List<Fortune> fortunes;

      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();
        fortunes = conn.Query<Fortune>(@"SELECT * FROM Fortune").ToList();
      }

      fortunes.Add(new Fortune { ID = 0, Message = "Additional fortune added at request time." });
      fortunes.Sort();

      const string header = "<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>";

      const string footer = "</table></body></html>";

      var body = string.Join("", fortunes.Select(x => "<tr><td>" + x.ID + "</td><td>" + x.Message + "</td></tr>"));

      var content =  header + body + footer;
      await response.WriteAsync(string.Format(RESPONSE, content.Length, "text/html", content));

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
    public static string datasource;
    public static SqliteConnection GetConnection()
    {
      return new SqliteConnection("Data Source=" + datasource);
    }
  
  }

}
