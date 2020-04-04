using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.PerformanceTesting;

namespace Performance
{
    public class SimpleTestFixture 
    {
        protected World m_PreviousWorld;
        protected World m_World;
        protected EntityManager m_Manager;


        [SetUp]
        virtual public void Setup()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            m_World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            m_Manager = m_World.EntityManager;
        }

        [TearDown]
        virtual public void TearDown()
        {
            if (m_Manager != null)
            {
                m_World.Dispose();
                m_World = null;

                World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
                m_PreviousWorld = null;
                m_Manager = null;
            }
        }
    }

    public enum CreationMethod
    {
        None = 0,
        IndividualArchetype,
        NativeArrayArchetype,
        CreateAndSetComponents,
        //CreateChunk,
        ECB_Individual,
        ECB_IndividualBurst,
    }

    public enum DestructionMethod
    {
        None = 0,
        IndividualEntity,
        WithQuery,
        WithNativeArray,
        ECB_WithQuery,
        ECB_Individual,
        ECB_IndividualBurst,
    }

    public struct ComparisonTestData2 : IComponentData
    {
        public int value;
    }

    public struct ComparisonTestData1 : IComponentData
    {
        public int value;
    }

    public class IsolatedComparison2 : SimpleTestFixture
    {
        [Test, Performance]
        public void CompareAllCombinations([Values(1, 25, 100, 250, 1000)] int eventsPerArchetype)
        {
            var system1 = m_World.GetOrCreateSystem<EventComparisonTestSystem>();
            system1.EventsPerArchetype = eventsPerArchetype;
            system1.Update();
        }

        [Test, Performance]
        public void CompareCreateAndDestroyOnly([Values(1, 25, 100, 250, 1000)] int eventsPerArchetype)
        {
            var system1 = m_World.GetOrCreateSystem<EventComparisonTestSystem>();
            system1.EventsPerArchetype = eventsPerArchetype;
            system1.GroupByNone = true;
            system1.Update();
        }

        [DisableAutoCreation]
        public class EventComparisonTestSystem : SystemBase
        {
            public int MeasurementCount = 100;
            public int Warmups = 5;
            public int EventsPerArchetype = 25;
            public NativeList<Entity> EntityBuffer;
            public NativeArray<ArchetypeChunk> ChunkBuffer;
            public IEnumerable<CreationMethod> CreationMethods;
            public IEnumerable<DestructionMethod> DestructionMethods;
            //public SampleGroupDefinition Group;
            public EntityQuery Query;
            public EntityArchetype Archetype;
            public List<Measurement> Measurements;
            public bool GroupByNone;

            //public List<Combination> Combinations;
            //public List<Stopwatch> Timers;

            protected override void OnCreate()
            {
                //Group = new SampleGroupDefinition
                //{
                //    AggregationType = AggregationType.Average,
                //    Name = "Unnamed",
                //    SampleUnit = SampleUnit.Millisecond
                //};
                var components = new[]
                {
                    ComponentType.ReadWrite<ComparisonTestData1>(),
                    ComponentType.ReadWrite<ComparisonTestData2>(),
                };
                Archetype = EntityManager.CreateArchetype(components);
                Query = EntityManager.CreateEntityQuery(components);

                EntityBuffer = new NativeList<Entity>(EventsPerArchetype, Allocator.Persistent);
                ChunkBuffer = new NativeArray<ArchetypeChunk>(100, Allocator.Persistent);

                CreationMethods = Enum.GetValues(typeof(CreationMethod)).Cast<CreationMethod>();
                DestructionMethods = Enum.GetValues(typeof(DestructionMethod)).Cast<DestructionMethod>();
            }

            protected override void OnDestroy()
            {
                EntityBuffer.Dispose();
                ChunkBuffer.Dispose();
            }

            public class Measurement
            {
                public Measurement(Combination combination)
                {
                    Combination = combination;
                }

                public Combination Combination;
                public SampleGroupDefinition Group;
                public List<Stopwatch> Timers = new List<Stopwatch>();
                public float Mean;
                public float Min;
                public float Max;
                public float StandardDeviation;
                public double Threshold;
                public double UpperClip;
                public double LowerClip;
            }

            public struct Combination : IEquatable<Combination>
            {
                public CreationMethod CreationType;
                public DestructionMethod DestructionType;

                public bool Equals(Combination other)
                {
                    return other.CreationType == CreationType && other.DestructionType == DestructionType;
                }

                public override int GetHashCode()
                {
                    int hash = 13;
                    hash = (hash * 7) + CreationType.GetHashCode();
                    hash = (hash * 7) + DestructionType.GetHashCode();
                    return hash;
                }
            }

            public Func<Combination, bool> Condition { get; set; }  = (c) => true;

            protected override void OnUpdate()
            {
                var entities = EntityBuffer;

                var archetype = Archetype;

                Measurements = CreationMethods.SelectMany(a => DestructionMethods.Select(b => new Combination
                {
                    CreationType = a,
                    DestructionType = b,

                })).Where(Condition).Distinct().Select(v => new Measurement(v)).ToList();

                //var fullChunksCount = EventsPerArchetype / archetype.ChunkCapacity;
                //var partialChunkCount = EventsPerArchetype % archetype.ChunkCapacity == 0 ? 0 : 1;
                //var totalChunks = fullChunksCount + partialChunkCount;
                //ChunkBuffer = new NativeList<ArchetypeChunk>(totalChunks, Allocator.Persistent);
                //ChunkBuffer.ResizeUninitialized(totalChunks);

                var chunks = ChunkBuffer;

                foreach (var item in Measurements)
                {
                    var combination = item.Combination;
                    var timers = item.Timers;



                    EntityManager.DestroyEntity(Query);

                    for (int i = 0; i < MeasurementCount; i++)
                    {
                        entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));
                        EntityManager.CreateEntity(archetype, entities);

                        // Seems to crash he editor if you create and delete from the same ECB.
                        var creationECB = new EntityCommandBuffer(Allocator.Persistent);
                        var destructionECB = new EntityCommandBuffer(Allocator.Persistent);

                        if (i < Warmups)
                        {
                            Test(combination, entities, chunks, archetype, Query, creationECB, destructionECB);
                        }
                        else
                        {
                            var sw = new Stopwatch();
                            timers.Add(sw);

                            sw.Restart();
                            Test(combination, entities, chunks, archetype, Query, creationECB, destructionECB);
                            sw.Stop();

                            //Measure.Custom(Group, sw.Elapsed.TotalMilliseconds);
                        }

                        creationECB.Dispose();
                        destructionECB.Dispose();
                    }
                }

                foreach (var item in Measurements)
                {
                    var combination = item.Combination;
                    var timers = item.Timers;

                    item.Max = (float)timers.Max(t => t.Elapsed.TotalMilliseconds);
                    item.Min = (float)timers.Min(t => t.Elapsed.TotalMilliseconds);
                    item.Mean = (float)timers.Average(t => t.Elapsed.TotalMilliseconds);
                    item.StandardDeviation = (float)StdDev(timers, t => t.Elapsed.TotalMilliseconds);
                    item.Threshold = item.StandardDeviation * 1.2;
                    item.UpperClip = item.Mean + item.Threshold;
                    item.LowerClip = item.Mean - item.Threshold;
                }

                if (GroupByNone)
                {
                    ProcessGroup(Measurements.Where(o => o.Combination.CreationType == CreationMethod.None && o.Combination.DestructionType != DestructionMethod.None));
                    ProcessGroup(Measurements.Where(o => o.Combination.DestructionType == DestructionMethod.None && o.Combination.CreationType != CreationMethod.None));
                }
                else
                {
                    foreach (var group in Measurements.GroupBy(g => g.Combination.CreationType))
                    {
                        ProcessGroup(group);
                    }
                }


            }

            private void ProcessGroup(IEnumerable<Measurement> group)
            {

                var orderedGroup = group.OrderBy(o => o.Mean).ToList();
                var groupMin = orderedGroup.First();

                for (int i = 0; i < orderedGroup.Count; i++)
                {
                    Measurement item = orderedGroup[i];
                    var combination = item.Combination;
                    var timers = item.Timers;

                    item.Group.Name = $"#{(i+1)}: Create: {item.Combination.CreationType}, Destroy: {item.Combination.DestructionType} (Entities={EventsPerArchetype}, Avg:{item.Mean:N4}ms, {(item.Mean/groupMin.Mean*100):0.##}%)";

                    item.Group.SampleUnit = SampleUnit.Millisecond;
                    item.Group.AggregationType = AggregationType.Average;

                    foreach (var timer in timers)
                    {
                        var time = timer.Elapsed.TotalMilliseconds;
                        if (time < item.LowerClip)
                            continue;

                        if (time > item.UpperClip)
                            continue;

                        Measure.Custom(item.Group, time);
                    }
                }
            
            }

            public static double StdDev<T>(IEnumerable<T> list, Func<T, double> values)
            {
                // ref: https://stackoverflow.com/questions/2253874/standard-deviation-in-linq
                // ref: https://stackoverflow.com/questions/2253874/linq-equivalent-for-standard-deviation
                // ref: http://warrenseen.com/blog/2006/03/13/how-to-calculate-standard-deviation/ 
                var mean = 0.0;
                var sum = 0.0;
                var stdDev = 0.0;
                var n = 0;
                foreach (var value in list.Select(values))
                {
                    n++;
                    var delta = value - mean;
                    mean += delta / n;
                    sum += delta * (value - mean);
                }
                if (1 < n)
                    stdDev = Math.Sqrt(sum / (n - 1));

                return stdDev;

            }

            private void Test(Combination combination, NativeList<Entity> entities, NativeArray<ArchetypeChunk> chunks, EntityArchetype archetype, EntityQuery query, EntityCommandBuffer creationECB, EntityCommandBuffer destructionECB)
            {
                switch (combination.DestructionType)
                {
                    case DestructionMethod.IndividualEntity:
                        for (int i = 0; i < entities.Length; i++)
                        {
                            EntityManager.DestroyEntity(entities[i]);
                        }
                        break;
                    case DestructionMethod.WithQuery:
                        EntityManager.DestroyEntity(query);
                        break;
                    case DestructionMethod.WithNativeArray:
                        EntityManager.DestroyEntity(entities);
                        break;
                    case DestructionMethod.ECB_WithQuery:
                        destructionECB.DestroyEntity(query);
                        destructionECB.Playback(EntityManager);
                        break;
                    case DestructionMethod.ECB_Individual:
                        for (int i = 0; i < entities.Length; i++)
                        {
                            destructionECB.DestroyEntity(entities[i]);
                        }
                        destructionECB.Playback(EntityManager);
                        break;
                    case DestructionMethod.ECB_IndividualBurst:
                        Job.WithCode(() =>
                        {
                            for (int i = 0; i < entities.Length; i++)
                            {
                                destructionECB.DestroyEntity(entities[i]);
                            }
                        }).Run();
                        destructionECB.Playback(EntityManager);
                        break;
                }

                switch (combination.CreationType)
                {
                    case CreationMethod.IndividualArchetype:
                        for (int i = 0; i < entities.Length; i++)
                        {
                            EntityManager.CreateEntity(archetype);
                        }
                        break;
                    //case CreationMethod.CreateChunk: // Throwing SizeOverflowInAllocator on 1000+ entities.
                    //    for (int i = 0; i < entities.Length; i++)
                    //    {
                    //        EntityManager.CreateChunk(archetype, chunks, entities.Length);
                    //    }
                    //    break;
                    case CreationMethod.NativeArrayArchetype:
                        EntityManager.CreateEntity(archetype, entities);
                        break;
                    case CreationMethod.CreateAndSetComponents:
                        for (int i = 0; i < entities.Length; i++)
                        {
                            var entity = EntityManager.CreateEntity();
                            EntityManager.AddComponent<ComparisonTestData2>(entity);
                            EntityManager.AddComponent<ComparisonTestData1>(entity);
                        }
                        break;
                    case CreationMethod.ECB_Individual:
                        for (int i = 0; i < entities.Length; i++)
                        {
                            creationECB.CreateEntity(archetype);
                        }
                        creationECB.Playback(EntityManager);
                        break;
                    case CreationMethod.ECB_IndividualBurst:
                        Job.WithCode(() =>
                        {
                            for (int i = 0; i < entities.Length; i++)
                            {
                                creationECB.CreateEntity(archetype);
                            }
                        }).Run();
                        creationECB.Playback(EntityManager);
                        break;
                }
            }
        }
    }
}
