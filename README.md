# Highly optimized .NET Core runtime for OpenWhisk

Start by reading ibm docs and running sample function: https://cloud.ibm.com/docs/openwhisk?topic=cloud-functions-prep#prep_dotnet

New method signature:
```
public static async Task Hello( HttpContext httpContext )
```

Few stats:
- cold start of empty function on IBM ~350ms
- cold start of 6kb function on IBM ~570ms
- cold start of 6kb function on IBM with their default runtime ~1450ms


I recommend adding this extension class to your project:
```
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class KSOpenWhiskExtension
{
	public static async Task WriteOWReponse( this HttpContext context, object content, int statusCode = 200, KeyValuePair<string, string>[] headers = null )
	{
		context.Response.StatusCode = 200; //this has to be 200 otherwise openwhisk fails
		string body = JsonSerializer.Serialize( new OWResponse(content, statusCode, headers));
		context.Response.ContentLength = Encoding.UTF8.GetByteCount( body );
		await context.Response.WriteAsync( body );
	}

	public class OWResponse
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

Using in IBM Cloud:
```
wsk action update {methodInfo.Name} out.zip --docker kamyker/openwhisk-action-dotnet-v3.1:stable --main {methodInfo.DeclaringType.Assembly.GetName().Name}::{methodInfo.DeclaringType.FullName}::{methodInfo.Name} --web true
```

It's important to set your proejct to netcoreapp3.1 and use AspNetCore framework. This is how .csproj should look like:
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
