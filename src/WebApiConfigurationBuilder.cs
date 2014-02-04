﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using ScriptCs.Contracts;

namespace ScriptCs.Hosting.WebApi
{
    public class WebApiConfigurationBuilder
    {
        private readonly HttpConfiguration _configuration;
        private readonly string _scriptsPath;
        private readonly IList<Func<string, ScriptClass>> _typeStrategies; 

        public WebApiConfigurationBuilder(HttpConfiguration configuration, string webBin)
        {
            _configuration = configuration;
            _scriptsPath = Path.Combine(webBin, "Scripts");
            _typeStrategies = new List<Func<string, ScriptClass>>();
        }

        public WebApiConfigurationBuilder AddTypeResolutionStrategy(Func<string,ScriptClass> strategy)
        {
            _typeStrategies.Add(strategy);
            return this;
        }

        public HttpConfiguration Build()
        {
            IList<Func<string, ScriptClass>> typeStrategies = new List<Func<string, ScriptClass>>(_typeStrategies);

            var console = new ScriptConsole();

            var configurator = new LoggerConfigurator(LogLevel.Debug);
            configurator.Configure(console);
            var logger = configurator.GetLogger();
            
            var services = new ScriptServicesBuilder(console, logger)
                .FilePreProcessor<WebApiFilePreProcessor>().Build();

            services.FileSystem.CurrentDirectory = _scriptsPath;
            
            var preProcessor = (WebApiFilePreProcessor) services.FilePreProcessor;
            typeStrategies.Add(ControllerStategy);
            preProcessor.SetClassStrategies(typeStrategies);
            preProcessor.LoadSharedCode(Path.Combine(_scriptsPath, "Shared"));
            ProcessScripts(services);
            return _configuration;
        }

        private void ProcessScripts(ScriptServices services)
        {
            IList<Type> controllers = new List<Type>();
            var packs = services.ScriptPackResolver.GetPacks().Union(new List<IScriptPack> {new WebApiScriptHack()});
            var scripts = services.FileSystem.EnumerateFiles(_scriptsPath, "*.csx", SearchOption.TopDirectoryOnly).ToList();

            var assemblyPaths = scripts
                .SelectMany(script => services.AssemblyResolver.GetAssemblyPaths(_scriptsPath, Path.GetFileName(script)))
                .Distinct();
            
            services.Executor.Initialize(assemblyPaths, packs);
            
            foreach (var script in scripts)
            {
                var result = services.Executor.Execute(script);
                var resultType = result.ReturnValue as Type;
                if (resultType != null)
                {
                    if (resultType.IsSubclassOf(typeof(ApiController)))
                    {
                        controllers.Add(resultType);
                    }
                }
            }

            var controllerResolver = new AssemblyControllerTypeResolver(controllers);
            _configuration.Services.Replace(typeof(IHttpControllerTypeResolver), controllerResolver);
        }

        private ScriptClass ControllerStategy(string name)
        {
            if (name.ToLower().EndsWith("controller.csx"))
            {
                return new ScriptClass
                    {
                        BaseType = "ApiController",
                        ClassName = Path.GetFileNameWithoutExtension(name)
                    };
            }
            return null;
        }
    }
}
