# Highly optimized .NET Core runtime for OpenWhisk

![openwhisk dotnet](https://www.ibm.com/blogs/cloud-archive/wp-content/uploads/2016/11/OpenWhisk-Hero-Image-800x400.png)

Start by reading ibm docs and running sample function: https://cloud.ibm.com/docs/openwhisk?topic=cloud-functions-prep#prep_dotnet

New method signature:
```
public static async Task Hello( HttpContext httpContext )
```

Few stats:
- cold start of empty function on IBM ~350ms
- cold start of 6mb function on IBM ~570ms
- cold start of 6mb function on IBM with their default runtime ~1450ms


I recommend adding this extension class to your project:
```
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class KSOpenWhiskExtension
{
	public static async Task<T> ReadOWRequestAsJsonObject<T>( this HttpContext context, JsonSerializerOptions options = null, CancellationToken token = default )
	{
		var req = await JsonSerializer.DeserializeAsync<OWRequest>( context.Request.Body, options, token );

		if ( string.IsNullOrEmpty( req.value.__ow_body ) )
			return default;

		byte[] byteArray = Convert.FromBase64String(req.value.__ow_body);

		using var stream = new MemoryStream( byteArray );
		return await JsonSerializer.DeserializeAsync<T>( stream );
	}

	public static async Task WriteOWResponse( this HttpContext context, object content, int statusCode = 200, KeyValuePair<string, string>[] headers = null, JsonSerializerOptions options = null )
	{
		context.Response.StatusCode = 200; //this has to be 200 otherwise openwhisk fails
		string body = JsonSerializer.Serialize<OWResponse>( new OWResponse(content, statusCode, headers), options);
		context.Response.ContentLength = Encoding.UTF8.GetByteCount( body );
		await context.Response.WriteAsync( body );
	}

	public struct OWRequest
	{
		public OWRequestValue value { get; set; }
		public struct OWRequestValue
		{
			public string __ow_body { get; set; } //body of request in base64
			public string __ow_query { get; set; } //normal string
		}
	}

	public struct OWResponse
	{
		public int statusCode { get; set; }
		public object body { get; set; }
		public KeyValuePair<string, string>[] headers;

		public OWResponse( object body, int statusCode, KeyValuePair<string, string>[] headers )
		{
			this.statusCode = statusCode;
			this.body = body;
			this.headers = headers;
		}
	}
}
```

Simple usage:
```
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Functions
{
	public static class HelloClass
	{
		public static async Task Hello( HttpContext httpContext )
		{
			await httpContext.WriteOWReponse( new { msg = "Hello my friend" } );
		}
	}
}
```


Updating action (for IBM Cloud change "wsk" to "ibmcloud fn"):
```
wsk action update {methodInfo.Name} out.zip --docker kamyker/openwhisk-action-dotnet-v3.1 --main {methodInfo.DeclaringType.Assembly.GetName().Name}::{methodInfo.DeclaringType.FullName}::{methodInfo.Name} --web raw
```
Use --web raw to be able to always read request using httpContext.ReadOWRequestAsJsonObject()


It's important to set your project to netcoreapp3.1 and use AspNetCore framework. This is how .csproj should look like:
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netcoreapp3.1</TargetFramework>
  </PropertyGroup>
  
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

# UnitTest class instead of using cmd
```
[TestClass]
public class UploadFunctions
{
    string classPath;
    MethodInfo methodInfo;
    MethodInfo GetMethodInfo( Func<HttpContext, Task> d )
    {
        return d.Method;
    }
 
    [TestMethod]
    public void Up_Hello()
    {
        methodInfo = GetMethodInfo( Functions.HelloClass.Hello );
        UploadMethod();
    }
 
    //always keep namespaceName == {namespaceName}.csproj
    public void UploadMethod()
    {
 
        var namespaceName = methodInfo.DeclaringType.Namespace;
 
        string path = $"<PATH_TO_YOUR_PROJECT_FOLDER>\\namespaceName}";
 
        var buildFolder = path + "\\out";
        if ( Directory.Exists( buildFolder ) )
            Directory.Delete( buildFolder, true );
        Directory.CreateDirectory( buildFolder );
 
        File.Delete( path + "\\out.zip" );
        RunCommands(
            new List<string>()
            {
                    $"dotnet restore {namespaceName}.csproj",
                    $"dotnet publish {namespaceName}.csproj -c Release -o    out",
                    @"cd out",
                    @"C:\Program Files\7-Zip\7z.exe a -r ..\out.zip *",
                    "pause"
            },
            path );
 
        RunCommands( new List<string>() {
                $"ibmcloud fn action update {methodInfo.Name} out.zip " +
                $"--docker kamyker/openwhisk-action-dotnet-v3.1:stable " +
                //$"--docker openwhisk/action-dotnet-v3.1 " +
                $"--main {methodInfo.DeclaringType.Assembly.GetName().Name}::{methodInfo.DeclaringType.FullName}::{methodInfo.Name} --web raw",
                "pause"
            }, path );
    }
 
    public static void RunCommands( List<string> cmds, string workingDirectory = "" )
    {
        var process = new Process();
        var psi = new ProcessStartInfo();
        psi.FileName = "cmd.exe";
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.WorkingDirectory = workingDirectory;
        process.StartInfo = psi;
        process.Start();
        process.OutputDataReceived += ( sender, e ) => { Trace.WriteLine( e.Data ); };
        process.ErrorDataReceived += ( sender, e ) => { Trace.WriteLine( e.Data ); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using ( StreamWriter sw = process.StandardInput )
        {
            foreach ( var cmd in cmds )
            {
                sw.WriteLine( cmd );
            }
        }
        process.WaitForExit();
    }
}
```
