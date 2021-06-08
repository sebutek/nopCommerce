﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BundlerMinifier;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Configuration;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Seo;
using Nop.Core.Infrastructure;
using Nop.Services.Seo;

namespace Nop.Web.Framework.UI
{
    /// <summary>
    /// Page head builder
    /// </summary>
    public partial class PageHeadBuilder : IPageHeadBuilder
    {
        #region Fields

        private static readonly object _lock = new object();

        private readonly AppSettings _appSettings;
        private readonly CommonSettings _commonSettings;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly INopFileProvider _fileProvider;
        private readonly IStaticCacheManager _staticCacheManager;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly SeoSettings _seoSettings;

        private readonly BundleFileProcessor _processor;
        private readonly List<string> _titleParts;
        private readonly List<string> _metaDescriptionParts;
        private readonly List<string> _metaKeywordParts;
        private readonly Dictionary<ResourceLocation, List<ScriptReferenceMeta>> _scriptParts;
        private readonly Dictionary<ResourceLocation, List<string>> _inlineScriptParts;
        private readonly Dictionary<ResourceLocation, List<CssReferenceMeta>> _cssParts;
        private readonly List<string> _canonicalUrlParts;
        private readonly List<string> _headCustomParts;
        private readonly List<string> _pageCssClassParts;
        private string _activeAdminMenuSystemName;
        private string _editPageUrl;

        #endregion

        #region Ctor

        public PageHeadBuilder(AppSettings appSettings,
            CommonSettings commonSettings,
            IActionContextAccessor actionContextAccessor,
            INopFileProvider fileProvider,
            IStaticCacheManager staticCacheManager,
            IUrlHelperFactory urlHelperFactory,
            IUrlRecordService urlRecordService,
            IWebHostEnvironment webHostEnvironment,
            SeoSettings seoSettings)
        {
            _appSettings = appSettings;
            _commonSettings = commonSettings;
            _actionContextAccessor = actionContextAccessor;
            _fileProvider = fileProvider;
            _staticCacheManager = staticCacheManager;
            _urlHelperFactory = urlHelperFactory;
            _urlRecordService = urlRecordService;
            _webHostEnvironment = webHostEnvironment;
            _seoSettings = seoSettings;

            _processor = new BundleFileProcessor();
            _titleParts = new List<string>();
            _metaDescriptionParts = new List<string>();
            _metaKeywordParts = new List<string>();
            _scriptParts = new Dictionary<ResourceLocation, List<ScriptReferenceMeta>>();
            _inlineScriptParts = new Dictionary<ResourceLocation, List<string>>();
            _cssParts = new Dictionary<ResourceLocation, List<CssReferenceMeta>>();
            _canonicalUrlParts = new List<string>();
            _headCustomParts = new List<string>();
            _pageCssClassParts = new List<string>();
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Get bundled file name
        /// </summary>
        /// <param name="parts">Parts to bundle</param>
        /// <returns>File name</returns>
        protected virtual async Task<string> GetBundleFileNameAsync(string[] parts)
        {
            if (parts == null || parts.Length == 0)
                throw new ArgumentException("parts");

            //calculate hash
            var hash = "";
            using (SHA256 sha = new SHA256Managed())
            {
                // string concatenation
                var hashInput = "";
                foreach (var part in parts)
                {
                    hashInput += part;
                    hashInput += ",";
                }

                var input = sha.ComputeHash(Encoding.Unicode.GetBytes(hashInput));
                hash = WebEncoders.Base64UrlEncode(input);
            }
            //ensure only valid chars
            hash = await _urlRecordService.GetSeNameAsync(hash, _seoSettings.ConvertNonWesternChars, _seoSettings.AllowUnicodeCharsInUrls);

            return hash;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Add title element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Title part</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddTitlePartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _titleParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append title element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Title part</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AppendTitlePartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _titleParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all title parts
        /// </summary>
        /// <param name="addDefaultTitle">A value indicating whether to insert a default title</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the Generated string
        /// </returns>
        public virtual Task<string> GenerateTitleAsync(bool addDefaultTitle)
        {
            var result = "";
            var specificTitle = string.Join(_seoSettings.PageTitleSeparator, _titleParts.AsEnumerable().Reverse().ToArray());
            if (!string.IsNullOrEmpty(specificTitle))
            {
                if (addDefaultTitle)
                {
                    //store name + page title
                    switch (_seoSettings.PageTitleSeoAdjustment)
                    {
                        case PageTitleSeoAdjustment.PagenameAfterStorename:
                            {
                                result = string.Join(_seoSettings.PageTitleSeparator, _seoSettings.DefaultTitle, specificTitle);
                            }
                            break;
                        case PageTitleSeoAdjustment.StorenameAfterPagename:
                        default:
                            {
                                result = string.Join(_seoSettings.PageTitleSeparator, specificTitle, _seoSettings.DefaultTitle);
                            }
                            break;

                    }
                }
                else
                {
                    //page title only
                    result = specificTitle;
                }
            }
            else
            {
                //store name only
                result = _seoSettings.DefaultTitle;
            }
            return Task.FromResult(result);
        }

        /// <summary>
        /// Add meta description element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Meta description part</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddMetaDescriptionPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _metaDescriptionParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append meta description element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Meta description part</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AppendMetaDescriptionPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _metaDescriptionParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all description parts
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual Task<string> GenerateMetaDescriptionAsync()
        {
            var metaDescription = string.Join(", ", _metaDescriptionParts.AsEnumerable().Reverse().ToArray());
            var result = !string.IsNullOrEmpty(metaDescription) ? metaDescription : _seoSettings.DefaultMetaDescription;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Add meta keyword element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Meta keyword part</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AddMetaKeywordPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _metaKeywordParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append meta keyword element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Meta keyword part</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AppendMetaKeywordPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _metaKeywordParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all keyword parts
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual Task<string> GenerateMetaKeywordsAsync()
        {
            var metaKeyword = string.Join(", ", _metaKeywordParts.AsEnumerable().Reverse().ToArray());
            var result = !string.IsNullOrEmpty(metaKeyword) ? metaKeyword : _seoSettings.DefaultMetaKeywords;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Add script element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="src">Script path (minified version)</param>
        /// <param name="debugSrc">Script path (full debug version). If empty, then minified version will be used</param>
        /// <param name="excludeFromBundle">A value indicating whether to exclude this script from bundling</param>
        /// <param name="isAsync">A value indicating whether to add an attribute "async" or not for js files</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddScriptPartsAsync(ResourceLocation location, string src, string debugSrc, bool excludeFromBundle, bool isAsync)
        {
            if (!_scriptParts.ContainsKey(location))
                _scriptParts.Add(location, new List<ScriptReferenceMeta>());

            if (string.IsNullOrEmpty(src))
                return Task.CompletedTask;

            if (string.IsNullOrEmpty(debugSrc))
                debugSrc = src;

            _scriptParts[location].Add(new ScriptReferenceMeta
            {
                ExcludeFromBundle = excludeFromBundle,
                IsAsync = isAsync,
                Src = src,
                DebugSrc = debugSrc
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append script element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="src">Script path (minified version)</param>
        /// <param name="debugSrc">Script path (full debug version). If empty, then minified version will be used</param>
        /// <param name="excludeFromBundle">A value indicating whether to exclude this script from bundling</param>
        /// <param name="isAsync">A value indicating whether to add an attribute "async" or not for js files</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AppendScriptPartsAsync(ResourceLocation location, string src, string debugSrc, bool excludeFromBundle, bool isAsync)
        {
            if (!_scriptParts.ContainsKey(location))
                _scriptParts.Add(location, new List<ScriptReferenceMeta>());

            if (string.IsNullOrEmpty(src))
                return Task.CompletedTask;

            if (string.IsNullOrEmpty(debugSrc))
                debugSrc = src;

            _scriptParts[location].Insert(0, new ScriptReferenceMeta
            {
                ExcludeFromBundle = excludeFromBundle,
                IsAsync = isAsync,
                Src = src,
                DebugSrc = debugSrc
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all script parts
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="bundleFiles">A value indicating whether to bundle script elements</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual async Task<string> GenerateScriptsAsync(ResourceLocation location, bool? bundleFiles = null)
        {
            if (!_scriptParts.ContainsKey(location) || _scriptParts[location] == null)
                return "";

            if (!_scriptParts.Any())
                return "";

            var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);

            var debugModel = _webHostEnvironment.IsDevelopment();

            if (!bundleFiles.HasValue)
            {
                //use setting if no value is specified
                bundleFiles = _commonSettings.EnableJsBundling;
            }

            if (bundleFiles.Value)
            {
                var partsToBundle = _scriptParts[location]
                    .Where(x => !x.ExcludeFromBundle)
                    .Distinct()
                    .ToArray();
                var partsToDontBundle = _scriptParts[location]
                    .Where(x => x.ExcludeFromBundle)
                    .Distinct()
                    .ToArray();

                var result = new StringBuilder();

                //parts to  bundle
                if (partsToBundle.Any())
                {
                    //ensure \bundles directory exists
                    _fileProvider.CreateDirectory(_fileProvider.GetAbsolutePath("bundles"));

                    var bundle = new Bundle();
                    foreach (var item in partsToBundle)
                    {
                        new PathString(urlHelper.Content(debugModel ? item.DebugSrc : item.Src))
                            .StartsWithSegments(urlHelper.ActionContext.HttpContext.Request.PathBase, out var path);
                        var src = path.Value.TrimStart('/');

                        //check whether this file exists, if not it should be stored into /wwwroot directory
                        if (!_fileProvider.FileExists(_fileProvider.MapPath(path)))
                            src = _fileProvider.Combine(_webHostEnvironment.WebRootPath, _fileProvider.Combine(src.Split("/").ToArray()));
                        else
                            src = _fileProvider.MapPath(path);

                        bundle.InputFiles.Add(src);
                    }

                    //output file
                    var outputFileName = await GetBundleFileNameAsync(partsToBundle.Select(x => debugModel ? x.DebugSrc : x.Src).ToArray());
                    bundle.OutputFileName = _fileProvider.Combine(_webHostEnvironment.WebRootPath, "bundles", outputFileName + ".js");
                    //save
                    var configFilePath = _fileProvider.MapPath($"/{outputFileName}.json");
                    bundle.FileName = configFilePath;

                    //performance optimization. do not bundle and minify for each HTTP request
                    //we periodically re-check already bundles file
                    //so if we have minification enabled, it could take up to several minutes to see changes in updated resource files (or just reset the cache or restart the site)
                    var cacheKey = new CacheKey($"Nop.minification.shouldrebuild.js-{outputFileName}")
                    {
                        CacheTime = _appSettings.CacheConfig.BundledFilesCacheTime
                    };

                    var shouldRebuild = await _staticCacheManager.GetAsync(_staticCacheManager.PrepareKey(cacheKey), () => true);

                    if (shouldRebuild)
                    {
                        lock (_lock)
                        {
                            //store json file to see a generated config file (for debugging purposes)
                            //BundleHandler.AddBundle(configFilePath, bundle);

                            //process
                            _processor.Process(configFilePath, new List<Bundle> { bundle });
                        }

                        await _staticCacheManager.SetAsync(cacheKey, false);
                    }

                    //render
                    result.AppendFormat("<script src=\"{0}\"></script>", urlHelper.Content("~/bundles/" + outputFileName + ".min.js"));
                    result.Append(Environment.NewLine);
                }

                //parts to not bundle
                foreach (var item in partsToDontBundle)
                {
                    var src = debugModel ? item.DebugSrc : item.Src;
                    result.AppendFormat("<script {1}src=\"{0}\"></script>", urlHelper.Content(src), item.IsAsync ? "async " : "");
                    result.Append(Environment.NewLine);
                }

                return result.ToString();
            }
            else
            {
                //bundling is disabled
                var result = new StringBuilder();
                foreach (var item in _scriptParts[location].Distinct())
                {
                    var src = debugModel ? item.DebugSrc : item.Src;
                    result.AppendFormat("<script {1}src=\"{0}\"></script>", urlHelper.Content(src), item.IsAsync ? "async " : "");
                    result.Append(Environment.NewLine);
                }
                return result.ToString();
            }
        }

        /// <summary>
        /// Add inline script element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="script">Script</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddInlineScriptPartsAsync(ResourceLocation location, string script)
        {
            if (!_inlineScriptParts.ContainsKey(location))
                _inlineScriptParts.Add(location, new List<string>());

            if (string.IsNullOrEmpty(script))
                return Task.CompletedTask;

            if (_inlineScriptParts[location].Contains(script))
                return Task.CompletedTask;

            _inlineScriptParts[location].Add(script);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append inline script element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="script">Script</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AppendInlineScriptPartsAsync(ResourceLocation location, string script)
        {
            if (!_inlineScriptParts.ContainsKey(location))
                _inlineScriptParts.Add(location, new List<string>());

            if (string.IsNullOrEmpty(script))
                return Task.CompletedTask;

            if (_inlineScriptParts[location].Contains(script))
                return Task.CompletedTask;

            _inlineScriptParts[location].Insert(0, script);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all inline script parts
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual Task<string> GenerateInlineScriptsAsync(ResourceLocation location)
        {
            if (!_inlineScriptParts.ContainsKey(location) || _inlineScriptParts[location] == null)
                return Task.FromResult("");

            if (!_inlineScriptParts.Any())
                return Task.FromResult("");

            var result = new StringBuilder();
            foreach (var item in _inlineScriptParts[location])
            {
                result.Append(item);
                result.Append(Environment.NewLine);
            }
            return Task.FromResult(result.ToString());
        }

        /// <summary>
        /// Add CSS element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="src">Script path (minified version)</param>
        /// <param name="debugSrc">Script path (full debug version). If empty, then minified version will be used</param>
        /// <param name="excludeFromBundle">A value indicating whether to exclude this script from bundling</param>
        public virtual Task AddCssFilePartsAsync(ResourceLocation location, string src, string debugSrc, bool excludeFromBundle = false)
        {
            if (!_cssParts.ContainsKey(location))
                _cssParts.Add(location, new List<CssReferenceMeta>());

            if (string.IsNullOrEmpty(src))
                return Task.CompletedTask;

            if (string.IsNullOrEmpty(debugSrc))
                debugSrc = src;

            _cssParts[location].Add(new CssReferenceMeta
            {
                ExcludeFromBundle = excludeFromBundle,
                Src = src,
                DebugSrc = debugSrc
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append CSS element
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="src">Script path (minified version)</param>
        /// <param name="debugSrc">Script path (full debug version). If empty, then minified version will be used</param>
        /// <param name="excludeFromBundle">A value indicating whether to exclude this script from bundling</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AppendCssFilePartsAsync(ResourceLocation location, string src, string debugSrc, bool excludeFromBundle = false)
        {
            if (!_cssParts.ContainsKey(location))
                _cssParts.Add(location, new List<CssReferenceMeta>());

            if (string.IsNullOrEmpty(src))
                return Task.CompletedTask;

            if (string.IsNullOrEmpty(debugSrc))
                debugSrc = src;

            _cssParts[location].Insert(0, new CssReferenceMeta
            {
                ExcludeFromBundle = excludeFromBundle,
                Src = src,
                DebugSrc = debugSrc
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all CSS parts
        /// </summary>
        /// <param name="location">A location of the script element</param>
        /// <param name="bundleFiles">A value indicating whether to bundle script elements</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual async Task<string> GenerateCssFilesAsync(ResourceLocation location, bool? bundleFiles = null)
        {
            if (!_cssParts.ContainsKey(location) || _cssParts[location] == null)
                return "";

            if (!_cssParts.Any())
                return "";

            var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);

            var debugModel = _webHostEnvironment.IsDevelopment();

            if (!bundleFiles.HasValue)
            {
                //use setting if no value is specified
                bundleFiles = _commonSettings.EnableCssBundling;
            }

            //CSS bundling is not allowed in virtual directories
            if (urlHelper.ActionContext.HttpContext.Request.PathBase.HasValue)
                bundleFiles = false;

            if (bundleFiles.Value)
            {
                var partsToBundle = _cssParts[location]
                    .Where(x => !x.ExcludeFromBundle)
                    .Distinct()
                    .ToArray();
                var partsToDontBundle = _cssParts[location]
                    .Where(x => x.ExcludeFromBundle)
                    .Distinct()
                    .ToArray();

                var result = new StringBuilder();


                //parts to  bundle
                if (partsToBundle.Any())
                {
                    //ensure \bundles directory exists
                    _fileProvider.CreateDirectory(_fileProvider.GetAbsolutePath("bundles"));

                    var bundle = new Bundle();
                    foreach (var item in partsToBundle)
                    {
                        new PathString(urlHelper.Content(debugModel ? item.DebugSrc : item.Src))
                            .StartsWithSegments(urlHelper.ActionContext.HttpContext.Request.PathBase, out var path);
                        var src = path.Value.TrimStart('/');

                        //check whether this file exists 
                        if (!_fileProvider.FileExists(_fileProvider.MapPath(path)))
                            src = _fileProvider.Combine(_webHostEnvironment.WebRootPath, _fileProvider.Combine(src.Split("/").ToArray()));
                        else
                            src = _fileProvider.MapPath(path);
                        bundle.InputFiles.Add(src);
                    }
                    //output file
                    var outputFileName = await GetBundleFileNameAsync(partsToBundle.Select(x => { return debugModel ? x.DebugSrc : x.Src; }).ToArray());
                    bundle.OutputFileName = _fileProvider.Combine(_webHostEnvironment.WebRootPath, "bundles", outputFileName + ".css");
                    //save
                    var configFilePath = _fileProvider.MapPath($"/{outputFileName}.json");
                    bundle.FileName = configFilePath;

                    //performance optimization. do not bundle and minify for each HTTP request
                    //we periodically re-check already bundles file
                    //so if we have minification enabled, it could take up to several minutes to see changes in updated resource files (or just reset the cache or restart the site)
                    var cacheKey = new CacheKey($"Nop.minification.shouldrebuild.css-{outputFileName}")
                    {
                        CacheTime = _appSettings.CacheConfig.BundledFilesCacheTime
                    };

                    var shouldRebuild = await _staticCacheManager.GetAsync(_staticCacheManager.PrepareKey(cacheKey), () => true);

                    if (shouldRebuild)
                    {
                        lock (_lock)
                        {
                            //store json file to see a generated config file (for debugging purposes)
                            //BundleHandler.AddBundle(configFilePath, bundle);

                            //process
                            _processor.Process(configFilePath, new List<Bundle> { bundle });
                        }

                        await _staticCacheManager.SetAsync(cacheKey, false);
                    }

                    //render
                    result.AppendFormat("<link href=\"{0}\" rel=\"stylesheet\" type=\"{1}\" />", urlHelper.Content("~/bundles/" + outputFileName + ".min.css"), MimeTypes.TextCss);
                    result.Append(Environment.NewLine);
                }

                //parts not to bundle
                foreach (var item in partsToDontBundle)
                {
                    var src = debugModel ? item.DebugSrc : item.Src;
                    result.AppendFormat("<link href=\"{0}\" rel=\"stylesheet\" type=\"{1}\" />", urlHelper.Content(src), MimeTypes.TextCss);
                    result.Append(Environment.NewLine);
                }

                return result.ToString();
            }
            else
            {
                //bundling is disabled
                var result = new StringBuilder();
                foreach (var item in _cssParts[location].Distinct())
                {
                    var src = debugModel ? item.DebugSrc : item.Src;
                    result.AppendFormat("<link href=\"{0}\" rel=\"stylesheet\" type=\"{1}\" />", urlHelper.Content(src), MimeTypes.TextCss);
                    result.AppendLine();
                }
                return result.ToString();
            }
        }

        /// <summary>
        /// Add canonical URL element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Canonical URL part</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddCanonicalUrlPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _canonicalUrlParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append canonical URL element to the <![CDATA[<head>]]>
        /// </summary>
        /// <param name="part">Canonical URL part</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AppendCanonicalUrlPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _canonicalUrlParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all canonical URL parts
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual Task<string> GenerateCanonicalUrlsAsync()
        {
            var result = new StringBuilder();
            foreach (var canonicalUrl in _canonicalUrlParts)
            {
                result.AppendFormat("<link rel=\"canonical\" href=\"{0}\" />", canonicalUrl);
                result.Append(Environment.NewLine);
            }
            return Task.FromResult(result.ToString());
        }

        /// <summary>
        /// Add any custom element to the <![CDATA[<head>]]> element
        /// </summary>
        /// <param name="part">The entire element. For example, <![CDATA[<meta name="msvalidate.01" content="123121231231313123123" />]]></param>
        /// <returns>A task that represents the asynchronous operation</returns>  
        public virtual Task AddHeadCustomPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _headCustomParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append any custom element to the <![CDATA[<head>]]> element
        /// </summary>
        /// <param name="part">The entire element. For example, <![CDATA[<meta name="msvalidate.01" content="123121231231313123123" />]]></param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AppendHeadCustomPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _headCustomParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all custom elements
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual async Task<string> GenerateHeadCustomAsync()
        {
            //use only distinct rows
            var distinctParts = await _headCustomParts.Distinct().ToListAsync();
            if (!distinctParts.Any())
                return "";

            var result = new StringBuilder();
            foreach (var path in distinctParts)
            {
                result.Append(path);
                result.Append(Environment.NewLine);
            }
            return result.ToString();
        }

        /// <summary>
        /// Add CSS class to the <![CDATA[<head>]]> element
        /// </summary>
        /// <param name="part">CSS class</param>
        /// <returns>A task that represents the asynchronous operation</returns> 
        public virtual Task AddPageCssClassPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _pageCssClassParts.Add(part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Append CSS class to the <![CDATA[<head>]]> element
        /// </summary>
        /// <param name="part">CSS class</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AppendPageCssClassPartsAsync(string part)
        {
            if (string.IsNullOrEmpty(part))
                return Task.CompletedTask;

            _pageCssClassParts.Insert(0, part);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate all title parts
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the generated string
        /// </returns>
        public virtual Task<string> GeneratePageCssClassesAsync()
        {
            var result = string.Join(" ", _pageCssClassParts.AsEnumerable().Reverse().ToArray());
            return Task.FromResult(result);
        }

        /// <summary>
        /// Specify "edit page" URL
        /// </summary>
        /// <param name="url">URL</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task AddEditPageUrlAsync(string url)
        {
            _editPageUrl = url;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Get "edit page" URL
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the url
        /// </returns>
        public virtual Task<string> GetEditPageUrlAsync()
        {
            return Task.FromResult(_editPageUrl);
        }

        /// <summary>
        /// Specify system name of admin menu item that should be selected (expanded)
        /// </summary>
        /// <param name="systemName">System name</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public virtual Task SetActiveMenuItemSystemNameAsync(string systemName)
        {
            _activeAdminMenuSystemName = systemName;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Get system name of admin menu item that should be selected (expanded)
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the system name
        /// </returns>
        public virtual Task<string> GetActiveMenuItemSystemNameAsync()
        {
            return Task.FromResult(_activeAdminMenuSystemName);
        }

        #endregion

        #region Nested classes

        /// <summary>
        /// JS file meta data
        /// </summary>
        private class ScriptReferenceMeta : IEquatable<ScriptReferenceMeta>
        {
            /// <summary>
            /// A value indicating whether to exclude the script from bundling
            /// </summary>
            public bool ExcludeFromBundle { get; set; }

            /// <summary>
            /// A value indicating whether to load the script asynchronously 
            /// </summary>
            public bool IsAsync { get; set; }

            /// <summary>
            /// Src for production
            /// </summary>
            public string Src { get; set; }

            /// <summary>
            /// Src for debugging
            /// </summary>
            public string DebugSrc { get; set; }

            /// <summary>
            /// Equals
            /// </summary>
            /// <param name="item">Other item</param>
            /// <returns>Result</returns>
            public bool Equals(ScriptReferenceMeta item)
            {
                if (item == null)
                    return false;
                return Src.Equals(item.Src) && DebugSrc.Equals(item.DebugSrc);
            }
            /// <summary>
            /// Get hash code
            /// </summary>
            /// <returns></returns>
            public override int GetHashCode()
            {
                return Src == null ? 0 : Src.GetHashCode();
            }
        }

        /// <summary>
        /// CSS file meta data
        /// </summary>
        private class CssReferenceMeta : IEquatable<CssReferenceMeta>
        {
            public bool ExcludeFromBundle { get; set; }

            /// <summary>
            /// Src for production
            /// </summary>
            public string Src { get; set; }

            /// <summary>
            /// Src for debugging
            /// </summary>
            public string DebugSrc { get; set; }

            /// <summary>
            /// Equals
            /// </summary>
            /// <param name="item">Other item</param>
            /// <returns>Result</returns>
            public bool Equals(CssReferenceMeta item)
            {
                if (item == null)
                    return false;
                return Src.Equals(item.Src) && DebugSrc.Equals(item.DebugSrc);
            }
            /// <summary>
            /// Get hash code
            /// </summary>
            /// <returns></returns>
            public override int GetHashCode()
            {
                return Src == null ? 0 : Src.GetHashCode();
            }
        }

        #endregion
    }
}