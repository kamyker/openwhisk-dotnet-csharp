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

using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Apache.OpenWhisk.Runtime.Common
{
    public class Init
    {
        public struct MethodToAdd
        {
            public Value value { get; set; }
            public struct Value
            {
                public string main { get; set; }
                public bool binary { get; set; }
                public string code { get; set; } // in Base64
            }
        }

        private readonly SemaphoreSlim _initSemaphoreSlim = new SemaphoreSlim(1, 1);

        public bool Initialized { get; private set; }
        private Type Type { get; set; }
        private MethodInfo Method { get; set; }
        private bool AwaitableMethod { get; set; }

        public Init()
        {
            Initialized = false;
            Type = null;
            Method = null;
        }

        public async Task<Run> HandleRequest(HttpContext httpContext)
        {
            await _initSemaphoreSlim.WaitAsync();
            try
            {
                if (Initialized)
                {
                    await httpContext.Response.WriteError("Cannot initialize the action more than once.");
                    Console.Error.WriteLine("Cannot initialize the action more than once.");
                    return (new Run(Method, AwaitableMethod));
                }

                //string body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
                //MethodToAdd.Value methodToAdd = JsonSerializer.Deserialize<MethodToAdd>(body).value;
                //MethodToAdd.Value methodToAdd = JsonConvert.DeserializeObject<MethodToAdd>(body).value;

                //fastest with much lower memory usage
                MethodToAdd.Value methodToAdd = (await System.Text.Json.JsonSerializer.DeserializeAsync<MethodToAdd>(httpContext.Request.Body)).value;


                if (string.IsNullOrEmpty(methodToAdd.main) ||
                    string.IsNullOrEmpty(methodToAdd.code))
                {
                    await httpContext.Response.WriteError("Missing main/no code to execute.");
                    return null;
                }

                if (!methodToAdd.binary )
                {
                    await httpContext.Response.WriteError("code must be binary (zip file).");
                    return null;
                }

                string[] mainParts = methodToAdd.main.Split("::");
                if (mainParts.Length != 3)
                {
                    await httpContext.Response.WriteError("main required format is \"Assembly::Type::Function\".");
                    return null;
                }

                string tempPath = Path.Combine(Environment.CurrentDirectory, Guid.NewGuid().ToString());
                try
                {
                    using ( var zipStream = new MemoryStream( Convert.FromBase64String( methodToAdd.code ) ) )
                    using ( var zip = new ZipArchive( zipStream ) )
                    {
                        zip.ExtractToDirectory( tempPath );
                    }
                }
                catch (Exception)
                {
                    await httpContext.Response.WriteError("Unable to decompress package.");
                    return null;
                }

                Environment.CurrentDirectory = tempPath;

                string assemblyFile = $"{mainParts[0]}.dll";

                string assemblyPath = Path.Combine(tempPath, assemblyFile);

                if (!File.Exists(assemblyPath))
                {
                    await httpContext.Response.WriteError($"Unable to locate requested assembly (\"{assemblyFile}\").");
                    return null;
                }

                try
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath);
                    Type = assembly.GetType(mainParts[1]);
                    if (Type == null)
                    {
                        await httpContext.Response.WriteError($"Unable to locate requested type (\"{mainParts[1]}\").");
                        return (null);
                    }
                    Method = Type.GetMethod(mainParts[2]);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    await httpContext.Response.WriteError(ex.Message
#if DEBUG
                                                          + ", " + ex.StackTrace
#endif
                    );
                    return (null);
                }

                if (Method == null)
                {
                    await httpContext.Response.WriteError($"Unable to locate requested method (\"{mainParts[2]}\").");
                    return (null);
                }

                Initialized = true;

                AwaitableMethod = (Method.ReturnType.GetMethod( nameof( Task.GetAwaiter ) ) != null);
                //AwaitableMethod = Method.ReturnType == typeof(Task);

                return (new Run(Method, AwaitableMethod));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString()) ;
                await httpContext.Response.WriteError( ex.ToString()
#if DEBUG
                                                  + ", " + ex.StackTrace
#endif
                );
                Startup.WriteLogMarkers();
                return (null);
            }
            finally
            {
                _initSemaphoreSlim.Release();
            }
        }
    }
}
