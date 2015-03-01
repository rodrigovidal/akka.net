﻿using System;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;

namespace Akka.Actor
{
    public class Deploy : IEquatable<Deploy>, ISurrogated
    {
        public static readonly Deploy Local = new Deploy(Scope.Local);
        public static readonly string NoDispatcherGiven = string.Empty;
        public static readonly string NoMailboxGiven = string.Empty;
        public static readonly Scope NoScopeGiven = null;
        public static readonly Deploy None = null;
        private readonly Config _config;
        private readonly string _dispatcher;
        private readonly string _mailbox;
        private readonly string _path;
        private readonly RouterConfig _routerConfig;
        private readonly Scope _scope;

        public Deploy()
        {
            _path = "";
            _config = ConfigurationFactory.Empty;
            _routerConfig = RouterConfig.NoRouter;
            _scope = NoScopeGiven;
            _dispatcher = NoDispatcherGiven;
            _mailbox = NoMailboxGiven;
        }

        public Deploy(string path, Scope scope)
            : this(scope)
        {
            _path = path;
        }

        public Deploy(Scope scope)
            : this()
        {
            _scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig) : this()
        {
            _routerConfig = routerConfig;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher)
            : this()
        {
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher,
            string mailbox)
            : this()
        {
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
            _mailbox = mailbox ?? NoMailboxGiven;
        }

        public string Path
        {
            get { return _path; }
        }

        public Config Config
        {
            get { return _config; }
        }

        public RouterConfig RouterConfig
        {
            get { return _routerConfig; }
        }

        public Scope Scope
        {
            get { return _scope; }
        }

        public string Mailbox
        {
            get { return _mailbox; }
        }

        public string Dispatcher
        {
            get { return _dispatcher; }
        }

        public bool Equals(Deploy other)
        {
            if (other == null) return false;
            return ((string.IsNullOrEmpty(Mailbox) && string.IsNullOrEmpty(other.Mailbox)) ||
                    string.Equals(Mailbox, other.Mailbox)) &&
                   string.Equals(Dispatcher, other.Dispatcher) &&
                   string.Equals(Path, other.Path) &&
                   RouterConfig.Equals(other.RouterConfig) &&
                   ((Config.IsNullOrEmpty() && other.Config.IsNullOrEmpty()) ||
                    Config.ToString().Equals(other.Config.ToString())) &&
                   (Scope == null && other.Scope == null || (Scope != null && Scope.Equals(other.Scope)));
        }

        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new DeploySurrogate
            {
                RouterConfig = RouterConfig,
                Scope = Scope,
                Path = Path,
                Config = Config,
                Mailbox = Mailbox,
                Dispatcher = Dispatcher
            };
        }

        public Deploy WithFallback(Deploy other)
        {
            return new Deploy
                (
                Path,
                Config.WithFallback(other.Config),
                RouterConfig.WithFallback(other.RouterConfig),
                Scope.WithFallback(other.Scope),
                Dispatcher == NoDispatcherGiven ? other.Dispatcher : Dispatcher,
                Mailbox == NoMailboxGiven ? other.Mailbox : Mailbox
                );
        }

        public Deploy WithScope(Scope scope)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                scope ?? Scope,
                Dispatcher,
                Mailbox
                );
        }

        public Deploy WithMailbox(string path)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                Dispatcher,
                path
                );
        }

        public Deploy WithDispatcher(string path)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                path,
                Mailbox
                );
        }

        public Deploy WithRouterConfig(RouterConfig routerConfig)
        {
            return new Deploy
                (
                Path,
                Config,
                routerConfig,
                Scope,
                Dispatcher,
                Mailbox
                );
        }

        /*
         path: String = "",
  config: Config = ConfigFactory.empty,
  routerConfig: RouterConfig = NoRouter,
  scope: Scope = NoScopeGiven,
  dispatcher: String = Deploy.NoDispatcherGiven,
  mailbox: String = Deploy.NoMailboxGiven)
         */

        public class DeploySurrogate : ISurrogate
        {
            public Scope Scope { get; set; }
            public RouterConfig RouterConfig { get; set; }
            public string Path { get; set; }
            public Config Config { get; set; }
            public string Mailbox { get; set; }
            public string Dispatcher { get; set; }

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Deploy(Path, Config, RouterConfig, Scope, Dispatcher, Mailbox);
            }
        }
    }
}