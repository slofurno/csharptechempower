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

namespace httplistener
{
  class Program
  {

    static byte[][] stack;
    static int cur = 0;
    static Dictionary<char, byte> lookup;

    static void Main(string[] args)
    {

      var bytes = new char[256];
      lookup = new Dictionary<char, byte>();

      for (var i = 0; i < 256; i++)
      {
        lookup[(char)i] = (byte)i;
      }

        stack = new byte[16][];
      for (int i = 0; i < 16; i++)
      {
        stack[i] = new byte[4096];
      }
      Process Proc = Process.GetCurrentProcess();
      Proc.ProcessorAffinity = (IntPtr)3;
      System.Net.ServicePointManager.DefaultConnectionLimit = int.MaxValue;
      System.Net.ServicePointManager.UseNagleAlgorithm = false;
      SqliteContext.datasource = "fortunes.sqlite";
      Listen().Wait();

    }

    static async Task Listen()
    {
      var listener = new HttpListener();
      listener.Prefixes.Add("http://*:8080/");
      listener.Start();

      while (true)
      {
        var context = await listener.GetContextAsync().ConfigureAwait(false);
        Serve(context);

      }
    }

    static async Task Serve(HttpListenerContext context)
    {
      var request = context.Request;
      using (var response = context.Response)
      {
        string responseString = null;

        switch (request.Url.LocalPath)
        {
          case "/plaintext":
            responseString = Plaintext(response);
            break;
          case "/json":
            responseString = Json(response);
            break;
          case "/db":
            responseString = Db(request, response);
            break;
          case "/fortunes":
            responseString = Fortunes(request, response);
            break;
          case "/queries":
            responseString = Queries(request, response);
            break;
          default:
            responseString = NotFound(response);
            break;
        }

        int n = ++cur & 15;
              
        response.ContentType = response.ContentType + "; charset=utf-8";
        //System.Text.Encoding.UTF8.GetBytes(responseString,0, len,stack[n],4096);

        var len = writeBinary(responseString, stack[n]);
        response.ContentLength64 = len;
        using (var output = response.OutputStream)
        {
          output.Write(stack[n], 0, len);//.ConfigureAwait(false);
          output.Flush();
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

    

    private static string NotFound(HttpListenerResponse response)
    {
      response.StatusCode = (int)HttpStatusCode.NotFound;
      response.ContentType = "text/plain";
      return "not found";
    }

    private static string Plaintext(HttpListenerResponse response)
    {
      response.ContentType = "text/plain";
      return "Hello, World!";
    }

    private static string Json(HttpListenerResponse response)
    {
      response.ContentType = "application/json";
      return JSON.SerializeDynamic(new { message = "Hello, World!" });

    }

    private static string Queries(HttpListenerRequest request, HttpListenerResponse response)
    {
      var count = GetQueries(request);
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

      return JSON.Serialize<RandomNumber[]>(results);

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

    private static string Db(HttpListenerRequest request, HttpListenerResponse response)
    {

      var rnd = new Random();
      var id = rnd.Next(10000);
      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();

        var result = conn.Query<RandomNumber>(@"SELECT * FROM World WHERE id=@id", new { id = id }).FirstOrDefault();

        return JSON.Serialize<RandomNumber>(result);
      }
    }

    
    private static string Fortunes(HttpListenerRequest request, HttpListenerResponse response)
    {
      List<Fortune> fortunes;

      using (var conn = SqliteContext.GetConnection())
      {
        conn.Open();
        fortunes = conn.Query<Fortune>(@"SELECT * FROM Fortune").ToList();
      }

      fortunes.Add(new Fortune { ID = 0, Message = "Additional fortune added at request time." });
      fortunes.Sort();

      response.ContentType = "text/html";

      const string header = "<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>";

      const string footer = "</table></body></html>";

      var body = string.Join("", fortunes.Select(x => "<tr><td>" + x.ID + "</td><td>" + x.Message + "</td></tr>"));

      return header + body + footer;
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
