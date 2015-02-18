using System.Collections.Generic;
using Akka.Util.Internal.Collections;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container, shared among all leaf actors.
    /// </summary>
    public class EmptyChildrenContainer : ChildrenContainer
    {
        private static readonly ImmutableTreeMap<string, ChildStats> EmptyStats = ImmutableTreeMap<string, ChildStats>.Empty;
        private static readonly ChildrenContainer _instance = new EmptyChildrenContainer();

        protected EmptyChildrenContainer()
        {
            //Intentionally left blank
        }

        public static ChildrenContainer Instance { get { return _instance; } }

        public virtual ChildrenContainer Add(string name, ChildRestartStats stats)
        {
            var newMap = EmptyStats.Add(name, stats);
            return NormalChildrenContainer.Create(newMap);
        }

        public ChildrenContainer Remove(ActorRef child)
        {
            return this;
        }

        public bool TryGetByName(string name, out ChildStats stats)
        {
            stats = null;
            return false;
        }

        public bool TryGetByRef(ActorRef actor, out ChildRestartStats childRestartStats)
        {
            childRestartStats = null;
            return false;
        }

        public bool Contains(ActorRef actor)
        {
            return false;
        }

        public IReadOnlyList<InternalActorRef> Children { get { return EmptyReadOnlyCollections<InternalActorRef>.List; } }

        public IReadOnlyList<ChildRestartStats> Stats { get { return EmptyReadOnlyCollections<ChildRestartStats>.List; } }

        public ChildrenContainer ShallDie(ActorRef actor)
        {
            return this;
        }

        public virtual ChildrenContainer Reserve(string name)
        {
            return NormalChildrenContainer.Create(EmptyStats.Add(name, ChildNameReserved.Instance));
        }

        public ChildrenContainer Unreserve(string name)
        {
            return this;
        }

        public override string ToString()
        {
            return "No children";
        }

        public virtual bool IsTerminating { get { return false; } }
        public virtual bool IsNormal { get { return true; } }
    }
}