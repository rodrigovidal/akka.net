﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public partial class ActorCell
    {
        private ChildrenContainer _childrenContainerDoNotCallMeDirectly = EmptyChildrenContainer.Instance;
        private long _nextRandomNameDoNotCallMeDirectly;

        [Obsolete("Use ChildrenContainer instead", true)]
        private ChildrenContainer ChildrenRefs
        {
            get { return ChildrenContainer; }
        }

        private ChildrenContainer ChildrenContainer
        {
            get { return _childrenContainerDoNotCallMeDirectly; }   //TODO: Hmm do we need memory barriers here???
        }

        private IReadOnlyCollection<ActorRef> Children
        {
            get { return ChildrenContainer.Children; }
        }

        private bool TryGetChild(string name, out ActorRef child)
        {
            ChildStats stats;
            if (ChildrenContainer.TryGetByName(name, out stats))
            {
                var restartStats = stats as ChildRestartStats;
                if (restartStats != null)
                {
                    child = restartStats.Child;
                    return true;
                }
            }
            child = null;
            return false;
        }

        public virtual ActorRef AttachChild(Props props, bool isSystemService, string name = null)
        {
            return ActorOf(props, name, true, isSystemService);
        }
        
        public virtual ActorRef ActorOf(Props props, string name = null)
        {
            return ActorOf(props, name, false, false);
        }

        private ActorRef ActorOf(Props props, string name, bool isAsync, bool isSystemService)
        {
            if (name == null)
                name = GetRandomActorName();
            else
                CheckName(name);

            return MakeChild(props, name, isAsync, isSystemService);
        }
        
        private string GetRandomActorName()
        {
            var id = Interlocked.Increment(ref _nextRandomNameDoNotCallMeDirectly);
            return "$" + id.Base64Encode();
        }

        /// <summary>
        ///     Stops the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        public void Stop(ActorRef child)
        {
            ChildRestartStats stats;
            if (ChildrenContainer.TryGetByRef(child, out stats))
            {
                var repointableActorRef = child as RepointableActorRef;
                if (repointableActorRef == null || repointableActorRef.IsStarted)
                {
                    UpdateChildrenRefs(c => c.ShallDie(child));
                }
            }
            ((InternalActorRef)child).Stop();
        }

        [Obsolete("Use UpdateChildrenRefs instead", true)]
        private void SwapChildrenRefs() { }


        /// <summary>
        /// Swaps out the children container, by calling <paramref name="updater"/>  to produce the new container.
        /// If the underlying container has been updated while <paramref name="updater"/> was called,
        /// <paramref name="updater"/> will be called again with the new container. This will repeat until the 
        /// container can be swapped out, or until <see cref="Tuple{T1,T2,T3}.Item1"/> contains <c>false</c>.
        /// <para>The returned tuple should contain:</para>
        /// <para>Item1: <c>true</c> if the container should be updated; <c>false</c> to not update and return Item3</para>
        /// <para>Item2: The new container (will only be used if Item1=<c>true</c>)</para>
        /// <para>Item3: The return value</para>
        /// </summary>
        /// <param name="updater">A function that returns a new container.</param>
        /// <returns>The third value of the tuple that <paramref name="updater"/> returned.</returns>
        private TReturn UpdateChildrenRefs<TReturn>(Func<ChildrenContainer, Tuple<bool, ChildrenContainer, TReturn>> updater)
        {
            return InterlockedSpin.ConditionallySwap(ref _childrenContainerDoNotCallMeDirectly, updater);
        }

        /// <summary>
        /// Swaps out the children container, by calling <paramref name="updater" />  to produce the new container.
        /// If the underlying container has been updated while <paramref name="updater" /> was called,
        /// <paramref name="updater" /> will be called again with the new container. This will repeat until the
        /// container can be swapped out.
        /// </summary>
        /// <param name="updater">A function that returns a new container.</param>
        /// <returns>The new updated <see cref="ChildrenContainer"/></returns>
        private ChildrenContainer UpdateChildrenRefs(Func<ChildrenContainer, ChildrenContainer> updater)
        {
            return InterlockedSpin.Swap(ref _childrenContainerDoNotCallMeDirectly, updater);
        }

        public void ReserveChild(string name)
        {
            UpdateChildrenRefs(c => c.Reserve(name));
        }

        protected void UnreserveChild(string name)
        {
            UpdateChildrenRefs(c => c.Unreserve(name));

        }

        /// <summary>This should only be used privately or when creating the root actor. </summary>
        public ChildRestartStats InitChild(InternalActorRef actor)
        {
            return UpdateChildrenRefs(cc =>
            {
                ChildStats stats;
                var name = actor.Path.Name;
                if (cc.TryGetByName(name, out stats))
                {
                    var old = stats as ChildRestartStats;
                    if (old != null)
                    {
                        //Do not update. Return old
                        return new Tuple<bool, ChildrenContainer, ChildRestartStats>(false, cc, old);
                    }
                    if (stats is ChildNameReserved)
                    {
                        var crs = new ChildRestartStats(actor);
                        var updatedContainer = cc.Add(name, crs);
                        //Update (if it's still cc) and return the new crs
                        return new Tuple<bool, ChildrenContainer, ChildRestartStats>(true, updatedContainer, crs);
                    }
                }
                //Do not update. Return null
                return new Tuple<bool, ChildrenContainer, ChildRestartStats>(false, cc, null);
            });
        }

        protected bool SetChildrenTerminationReason(SuspendReason reason)
        {
            return UpdateChildrenRefs(cc =>
            {
                var c = cc as TerminatingChildrenContainer;
                if (c != null)
                    //The arguments says: Update; with a new reason; and return true
                    return new Tuple<bool, ChildrenContainer, bool>(true, c.CreateCopyWithReason(reason), true);
                
                //The arguments says:Do NOT update; any container will do since it wont be updated; return false 
                return new Tuple<bool, ChildrenContainer, bool>(false, cc, false);
            });
        }

        protected void SetTerminated()
        {
            UpdateChildrenRefs(c => TerminatedChildrenContainer.Instance);
        }

        protected bool IsNormal { get { return ChildrenContainer.IsNormal; } }
        protected bool IsTerminating { get { return ChildrenContainer.IsTerminating; } }

        private bool IsWaitingForChildren  // This is called isWaitingForChildrenOrNull in AkkaJVM but is used like if returned a bool
        {
            get
            {
                var terminating = ChildrenContainer as TerminatingChildrenContainer;
                return terminating != null && terminating.Reason is SuspendReason.WaitingForChildren;
            }
        }

        /// <summary>
        ///     Suspends the children.
        /// </summary>
        private void SuspendChildren(List<ActorRef> exceptFor = null)
        {
            var except = exceptFor ?? Enumerable.Empty<ActorRef>();
            (from s in ChildrenContainer.Stats
             where !except.Contains(s.Child)
             select s.Child)
            .ToList()
            .ForEach(c => c.Suspend());
        }

        /// <summary>
        ///     Resumes the children.
        /// </summary>
        private void ResumeChildren(Exception causedByFailure, ActorRef perpetrator)
        {
            foreach (var stats in ChildrenContainer.Stats)
            {
                var child = stats.Child;
                var cause = child.Equals(perpetrator) ? causedByFailure : null;
                child.Resume(cause);
            }
        }

        /// <summary>
        /// Tries to get the stats for the child with the specified name. The stats can be either <see cref="ChildNameReserved"/> 
        /// indicating that only a name has been reserved for the child, or a <see cref="ChildRestartStats"/> for a child that 
        /// has been initialized/created.
        /// </summary>
        public bool TryGetChildStatsByName(string name, out ChildStats child)   //This is called getChildByName in Akka JVM
        {
            return ChildrenContainer.TryGetByName(name, out child);
        }

        /// <summary>
        /// Tries to get the stats for the child with the specified name. This ignores children for whom only names have been reserved.
        /// </summary>
        private bool TryGetChildRestartStatsByName(string name, out ChildRestartStats child)
        {
            ChildStats stats;
            if (ChildrenContainer.TryGetByName(name, out stats))
            {
                child = stats as ChildRestartStats;
                if (child != null)
                    return true;
            }
            child = null;
            return false;
        }

        /// <summary>
        /// Tries to get the stats for the specified child.
        /// <remarks>Since the child exists <see cref="ChildRestartStats"/> is the only valid <see cref="ChildStats"/>.</remarks>
        /// </summary>
        protected bool TryGetChildStatsByRef(ActorRef actor, out ChildRestartStats child)   //This is called getChildByRef in Akka JVM
        {
            return ChildrenContainer.TryGetByRef(actor, out child);
        }

        // In Akka JVM there is a getAllChildStats here. Use ChildrenRefs.Stats instead

        [Obsolete("Use TryGetSingleChild")]
        public InternalActorRef GetSingleChild(string name)
        {
            InternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRef.Nobody;
        }

        public bool TryGetSingleChild(string name, out InternalActorRef child)
        {
            var nameAndUid = SplitNameAndUid(name);
            ChildRestartStats stats;
            if (TryGetChildRestartStatsByName(nameAndUid.Name, out stats))
            {
                var uid = nameAndUid.Uid;
                if (uid == ActorCell.UndefinedUid || uid == stats.Uid)
                {
                    child = stats.Child;
                    return true;
                }
            }
            child = ActorRef.Nobody;
            return false;
        }

        protected SuspendReason RemoveChildAndGetStateChange(ActorRef child)
        {
            var terminating = ChildrenContainer as TerminatingChildrenContainer;
            if (terminating != null)
            {
                var newContainer = UpdateChildrenRefs(c => c.Remove(child));
                if (newContainer is TerminatingChildrenContainer) return null;
                return terminating.Reason;
            }
            UpdateChildrenRefs(c => c.Remove(child));
            return null;
        }

        private void CheckName(string name)
        {
            if (name == null) throw new InvalidActorNameException("Actor name must not be null.");
            if (name.Length == 0) throw new InvalidActorNameException("Actor name must not be empty.");
            if (!ActorPath.IsValidPathElement(name))
                throw new InvalidActorNameException(string.Format("Illegal actor name [{0}]. Actor paths MUST: not start with `$`, include only ASCII letters and can only contain these special characters: ${1}.", name, new String(ActorPath.ValidSymbols)));
        }

        private InternalActorRef MakeChild(Props props, string name, bool async, bool systemService)
        {
            //TODO: Implement SerializeAllCreators
            //   if (cell.system.settings.SerializeAllCreators && !systemService && props.deploy.scope != LocalScope)
            //     try {
            //       val ser = SerializationExtension(cell.system)
            //       props.args forall (arg ⇒
            //         arg == null ||
            //           arg.isInstanceOf[NoSerializationVerificationNeeded] ||
            //           ser.deserialize(ser.serialize(arg.asInstanceOf[AnyRef]).get, arg.getClass).get != null)
            //     } catch {
            //       case NonFatal(e) ⇒ throw new IllegalArgumentException(s"pre-creation serialization check failed at [${cell.self.path}/$name]", e)
            //     }

            // In case we are currently terminating, fail external attachChild requests
            // (internal calls cannot happen anyway because we are suspended)
            if (IsTerminating)
                throw new InvalidOperationException("Cannot create child while terminating or terminated");
            //reserve the name before we create the actor
            ReserveChild(name);
            InternalActorRef actor;
            try
            {
                var childPath = new ChildActorPath(Self.Path, name, NewUid());
                actor = _systemImpl.Provider.ActorOf(_systemImpl, props, _self, childPath, systemService: systemService, deploy: null, lookupDeploy: true, async: async);
            }
            catch
            {
                //if actor creation failed, unreserve the name
                UnreserveChild(name);
                throw;
            }
            //TODO: When Mailbox has SuspendCount implement this
            //      // mailbox==null during RoutedActorCell constructor, where suspends are queued otherwise
            //      if (mailbox ne null) for (_ ← 1 to mailbox.suspendCount) actor.suspend()

            //replace the reservation with the real actor
            InitChild(actor);
            actor.Start();
            return actor;
        }
    }
}