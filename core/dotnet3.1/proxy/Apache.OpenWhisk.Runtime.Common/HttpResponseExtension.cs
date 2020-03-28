/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Apache.OpenWhisk.Runtime.Common
{
	public static class HttpResponseExtension
	{
		public static async Task WriteResponse( this HttpResponse response, int code, object content )
		{
			response.StatusCode = code;

			string body = JsonConvert.SerializeObject( new Response(content) );
			response.ContentLength = Encoding.UTF8.GetByteCount( body );
			await response.WriteAsync( body );
			//same as response.WriteAsync( body ) //https://github.com/aspnet/HttpAbstractions/blob/master/src/Microsoft.AspNetCore.Http.Abstractions/Extensions/HttpResponseWritingExtensions.cs
			//byte[] data = Encoding.UTF8.GetBytes(body);
			//await response.Body.WriteAsync( data, 0, data.Length, default( CancellationToken ) ); //for concurrency
			//response.Body.Write( data, 0, data.Length );

			//using ( StreamWriter writer = new StreamWriter( response.Body, leaveOpen: true ) )
			//using ( JsonTextWriter jsonWriter = new JsonTextWriter( writer ) )
			//{
			//	JsonSerializer ser = new JsonSerializer();
			//	ser.Serialize( jsonWriter, content );
			//	jsonWriter.Flush();
			//}
			//response.ContentLength = response.Body.Length;
		}

		public static async Task WriteError( this HttpResponse response, string errorMessage )
		{
			JObject message = new JObject {{"error", new JValue(errorMessage)}};
			string body = JsonConvert.SerializeObject( message );
			response.ContentLength = Encoding.UTF8.GetByteCount( body );
			response.StatusCode = 502;
			await response.WriteAsync( body );
			//await response.WriteAsync( JsonConvert.SerializeObject( new Response( message ) ) );
			//await WriteResponse( response, 502, message );
		}


		public class Response
		{
			public object body { get; set; }

			public Response( object body )
			{
				this.body = body;
			}
		}

	}
}
