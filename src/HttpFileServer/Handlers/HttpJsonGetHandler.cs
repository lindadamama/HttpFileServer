﻿using HttpFileServer.Core;
using HttpFileServer.Services;
using HttpFileServer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HttpFileServer.Handlers
{
    public class HttpJsonGetHandler : HttpGetHandler
    {
        #region Fields

        private CacheService _cacheSrv;
        private JsonService _jsonSrv;

        #endregion Fields

        #region Constructors

        public HttpJsonGetHandler(string rootDir, CacheService cacheSrv) : base(rootDir, cacheSrv)
        {
            _cacheSrv = cacheSrv;

            _jsonSrv = new JsonService();
        }

        #endregion Constructors

        #region Methods

        public override async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var tmp = Path.Combine(SourceDir, request.Url.LocalPath.TrimStart('/'));
            var dstpath = tmp.Replace('/', '\\');

            if (request.AcceptTypes == null)
            {
                await base.ProcessRequest(context);
                return;
            }
            var useJson = request.AcceptTypes.Any(p => p.Equals("application/json", StringComparison.OrdinalIgnoreCase));
            if (!useJson)
            {
                await base.ProcessRequest(context);
                return;
            }

            //IfNoMatchCheck
            var requestETag = request.Headers["If-None-Match"];
            var cacheTag = _cacheSrv.GetPathCacheId(dstpath);

            if (requestETag == cacheTag)
            {
                response.StatusCode = (int)HttpStatusCode.NotModified;
            }
            else
            {
                response.AppendHeader("Cache-Control", "no-cache");
                response.AppendHeader("Etag", cacheTag);
                //文件
                if (File.Exists(dstpath))
                {
                    if (request.Headers.AllKeys.Count(p => p.ToLower() == "range") > 0)
                        await ResponseContentPartial(dstpath, request, response);
                    else
                        await ResponseContentFull(dstpath, request, response);
                    return;
                }

                //目录
                response.AddHeader("Content-Type", "application/json");

                var buff = _cacheSrv.GetCache(dstpath);

                if (buff is null)
                {
                    DirInfoResponse respObj = new DirInfoResponse();
                    var dirinfo = new DirectoryInfo(dstpath);
                    respObj.LastWriteTime = dirinfo.LastWriteTime;
                    respObj.Name = dirinfo.Name;
                    respObj.RelativePath = Path.GetFullPath(dstpath).Replace(SourceDir, "/");

                    var dirs = Directory.GetDirectories(dstpath);
                    var files = Directory.GetFiles(dstpath);
                    var dlst = new List<DirPathInfo>();
                    var flst = new List<FilePathInfo>();
                    foreach (var p in dirs)
                    {
                        var dinfo = p.GetPathInfo(SourceDir) as DirPathInfo;
                        dlst.Add(dinfo);
                    }
                    foreach (var f in files)
                    {
                        var finfo = f.GetPathInfo(SourceDir) as FilePathInfo;
                        flst.Add(finfo);
                    }
                    respObj.Directories = dlst.ToArray();
                    respObj.Files = flst.ToArray();

                    var content = _jsonSrv.SerializeObject(respObj);
                    buff = Encoding.UTF8.GetBytes(content);
                    _cacheSrv.SaveCache(dstpath, buff);
                }

                response.ContentLength64 = buff.LongLength;
                var stream = new MemoryStream(buff);
                await stream.CopyToAsync(response.OutputStream);
            }
        }

        #endregion Methods
    }
}