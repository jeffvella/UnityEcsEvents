using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using UnityEngine;
using Vella.Events;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

namespace Performance
{
    class IntegrationPerformanceTests : ECSTestsFixture
    {

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateRandomEvents([Values(5, 25, 100, 1000)] int eventsPerArchetype, [Values(1, 10, 50, 100)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            var eMin = 1;
            var eMax = eventsPerArchetype;

            var aMin = 1;
            var aMax = archetypeCount;

            var group = new SampleGroup($"Random entity counts between [{eMin}-{eMax}], and [{aMin}-{aMax}] archetypes", SampleUnit.Millisecond);

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();


            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.EventsPerArchetype = UnityEngine.Random.Range(eMin, eMax);
                    system.ArchetypeCount = UnityEngine.Random.Range(aMin, aMax);
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.EventsPerArchetype = UnityEngine.Random.Range(eMin, eMax);
                    system.ArchetypeCount = UnityEngine.Random.Range(aMin, aMax);
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }

        }

        //[Test, Performance, TestCategory(TestCategory.Performance)]
        //public void CompareToDefaultApproaches([Values(1, 25, 100, 250, 1000, 5000)] int eventsPerArchetype)
        //{
        //    // * I randomized event counts because its an unfair comparison otherwise: ECS Events will not
        //    // create or destroy any entities if the required amount doesn't change.

        //    var system = World.GetOrCreateSystem<EntityEventSystem>();
        //    var components = new[]
        //    {
        //        ComponentType.ReadWrite<EntityEvent>(),
        //        ComponentType.ReadWrite<EcsTestData>(),
        //    };
        //    var archetype = Manager.CreateArchetype(components);
        //    var query = Manager.CreateEntityQuery(components);

        //    var groupA = new SampleGroupDefinition
        //    {
        //        AggregationType = AggregationType.Average,
        //        Name = "Default",
        //        SampleUnit = SampleUnit.Millisecond
        //    };

        //    var measurements = 100;
        //    var warmups = 5;

        //    //var sw = new Stopwatch();
        //    var entities = new NativeList<Entity>(eventsPerArchetype, Allocator.Temp);
        //    //for (int i = 0; i < measurements; i++)
        //    //{
        //    //    entities.ResizeUninitialized(UnityEngine.Random.Range(1, eventsPerArchetype));
        //    //    if (i < warmups)
        //    //    {
        //    //        Manager.CreateEntity(archetype, entities);
        //    //        entities.Clear();
        //    //    }
        //    //    else
        //    //    {
        //    //        sw.Restart();
        //    //        Manager.DestroyEntity(query);
        //    //        Manager.CreateEntity(archetype, entities);
        //    //        for (int j = 0; j < entities.Length; j++)
        //    //        {
        //    //            Manager.SetComponentData<EcsTestData>(entities[j], default);
        //    //        }
        //    //        sw.Stop();
        //    //        Measure.Custom(groupA, sw.Elapsed.TotalMilliseconds);
        //    //    }
        //    //}

        //    //var queue = system.GetQueue<EcsTestData>();
        //    //var groupB = new SampleGroupDefinition
        //    //{
        //    //    AggregationType = AggregationType.Average,
        //    //    Name = "ECS Events",
        //    //    SampleUnit = SampleUnit.Millisecond
        //    //};

        //    //for (int i = 0; i < measurements; i++)
        //    //{
        //    //    entities.ResizeUninitialized(UnityEngine.Random.Range(1, eventsPerArchetype));
        //    //    if (i < warmups)
        //    //    {
        //    //        queue.Enqueue(default(EcsTestData));
        //    //        EventSystem.Update();
        //    //    }
        //    //    else
        //    //    {
        //    //        sw.Restart();
        //    //        for (int j = 0; j < entities.Length; j++)
        //    //        {
        //    //            queue.Enqueue(default(EcsTestData));
        //    //        }
        //    //        EventSystem.Update();
        //    //        sw.Stop();
        //    //        Measure.Custom(groupB, sw.Elapsed.TotalMilliseconds);
        //    //    }
        //    //}

        //    //var groupC = new SampleGroupDefinition
        //    //{
        //    //    AggregationType = AggregationType.Average,
        //    //    Name = GetName("EntityCommandBuffer"),
        //    //    SampleUnit = SampleUnit.Millisecond
        //    //};

        //    //for (int i = 0; i < measurements; i++)
        //    //{
        //    //    entities.ResizeUninitialized(UnityEngine.Random.Range(1, eventsPerArchetype));
        //    //    if (i < warmups)
        //    //    {

        //    //    }
        //    //    else
        //    //    {
        //    //        var ecb = new EntityCommandBuffer(Allocator.Temp);
        //    //        sw.Restart();
        //    //        Manager.DestroyEntity(query);
        //    //        for (int j = 0; j < entities.Length; j++)
        //    //        {
        //    //            ecb.CreateEntity(archetype);
        //    //        }
        //    //        ecb.Playback(Manager);
        //    //        sw.Stop();
        //    //        Measure.Custom(groupC, sw.Elapsed.TotalMilliseconds);
        //    //    }
        //    //}

        //    //var system1 = World.GetOrCreateSystem<BurstECBEventQueueSystem>();
        //    //system1.Measurements = measurements;
        //    //system1.Warmups = warmups;
        //    //system1.EventsPerArchetype = eventsPerArchetype;
        //    //system1.CreatedEntities = entities;
        //    //system1.Query = query;
        //    //system1.Archetype = archetype;
        //    //system1.Update();

        //    //var system1 = World.GetOrCreateSystem<EventComparisonTestSystem>();
        //    //system1.Measurements = measurements;
        //    //system1.Warmups = warmups;
        //    //system1.EventsPerArchetype = eventsPerArchetype;
        //    //system1.CreatedEntities = entities;
        //    //system1.Query = query;
        //    //system1.Archetype = archetype;
        //    //system1.ECB_BurstQueued_IndividuallyDestroyed();
        //    //system1.ECB_BurstQueued_ECBQueryDestroyed();

        //    //var system1 = World.GetOrCreateSystem<EventComparisonTestSystem>();
        //    //system1.EventsPerArchetype = eventsPerArchetype;
        //    //system1.Update();
        //}

        //[Test, Performance]
        //public void CompareDefaultApproaches([Values(1, 25, 100, 250, 1000, 5000)] int eventsPerArchetype)
        //{
        //    var system1 = World.GetOrCreateSystem<EventComparisonTestSystem>();
        //    system1.EventsPerArchetype = eventsPerArchetype;
        //    system1.Update();
        //}

        //[DisableAutoCreation]
        //public class EventComparisonTestSystem : SystemBase
        //{
        //    public EntityCommandBuffer ECB;
        //    protected override void OnCreate()
        //    {
        //        Group = new SampleGroupDefinition
        //        {
        //            AggregationType = AggregationType.Average,
        //            Name = "EntityCommandBuffer QueuedFromJob Destroyed Individually Inside Burst",
        //            SampleUnit = SampleUnit.Millisecond
        //        };
        //        StopWatch = new Stopwatch();
        //        var components = new[]
        //        {
        //            ComponentType.ReadWrite<EntityEvent>(),
        //            ComponentType.ReadWrite<EcsTestData>(),
        //        };
        //        Archetype = EntityManager.CreateArchetype(components);
        //        Query = EntityManager.CreateEntityQuery(components);

        //        var groupA = new SampleGroupDefinition
        //        {
        //            AggregationType = AggregationType.Average,
        //            Name = "Default",
        //            SampleUnit = SampleUnit.Millisecond
        //        };

        //        Measurements = 100;
        //        Warmups = 5;
        //        EntityBuffer = new NativeList<Entity>(EventsPerArchetype, Allocator.Temp);
        //    }

        //    public int Measurements = 100;
        //    public int Warmups = 5;
        //    public int EventsPerArchetype = 25;
        //    public NativeList<Entity> EntityBuffer;
        //    public SampleGroupDefinition Group;
        //    public EntityQuery Query;
        //    public EntityArchetype Archetype;
        //    public Stopwatch StopWatch;

        //    public enum CreationMethod
        //    {
        //        None = 0,
        //        IndividualArchetype,
        //        NativeArrayArchetype,
        //        CreateAndSetComponents,
        //        ECB_NativeArrayArchetype,
        //        ECB_Individual,
        //        ECB_IndividualBurst,
        //        ECB_IndividualBurstQueued,
        //    }

        //    public enum DestructionMethod
        //    {
        //        None = 0,
        //        IndividualEntity,
        //        WithQuery,
        //        WithNativeArray,
        //        ECB_WithQuery,
        //        ECB_Individual,
        //        ECB_IndividualBurst,
        //    }

        //    public struct Combination
        //    {
        //        public CreationMethod CreationType;
        //        public DestructionMethod DestructionType;
        //    }

        //    protected override void OnUpdate()
        //    {
        //        var entities = EntityBuffer;
        //        var archetype = Archetype;

        //        var creationMethods = Enum.GetValues(typeof(CreationMethod)).Cast<CreationMethod>().Skip(1);
        //        var destructionMethods = Enum.GetValues(typeof(DestructionMethod)).Cast<DestructionMethod>().Skip(1);
        //        var combinations = creationMethods.SelectMany(a => destructionMethods.Select(b => new Combination
        //        {
        //            CreationType = a,
        //            DestructionType = b,
        //        })).ToList();

        //        foreach (var combination in combinations)
        //        {
        //            Group.Name = $"Create: {combination.CreationType}, Destroy: {combination.DestructionType}";

        //            EntityManager.DestroyEntity(Query);

        //            for (int i = 0; i < Measurements; i++)
        //            {
        //                entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));
        //                EntityManager.CreateEntity(archetype, entities);

        //                // Seems to crash he editor if you create and delete from the same ECB.
        //                var creationECB = new EntityCommandBuffer(Allocator.Temp);
        //                var destructionECB = new EntityCommandBuffer(Allocator.Temp);

        //                if (i < Warmups)
        //                {
        //                    Test(combination, entities, archetype, Query, creationECB, destructionECB);
        //                }
        //                else
        //                {
        //                    StopWatch.Restart();
        //                    Test(combination, entities, archetype, Query, creationECB, destructionECB);
        //                    StopWatch.Stop();
        //                    Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //                }
        //            }
        //        }
        //    }

        //    private void Test(Combination combination, NativeList<Entity> entities, EntityArchetype archetype, EntityQuery query, EntityCommandBuffer creationECB, EntityCommandBuffer destructionECB)
        //    {
        //        switch (combination.DestructionType)
        //        {
        //            case DestructionMethod.IndividualEntity:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    EntityManager.DestroyEntity(entities[i]);
        //                }
        //                break;
        //            case DestructionMethod.WithQuery:
        //                EntityManager.DestroyEntity(query);
        //                break;
        //            case DestructionMethod.WithNativeArray:
        //                EntityManager.DestroyEntity(entities);
        //                break;
        //            case DestructionMethod.ECB_WithQuery:
        //                destructionECB.DestroyEntity(query);
        //                destructionECB.Playback(EntityManager);
        //                break;
        //            case DestructionMethod.ECB_Individual:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    destructionECB.DestroyEntity(entities[i]);
        //                }
        //                destructionECB.Playback(EntityManager);
        //                break;
        //            case DestructionMethod.ECB_IndividualBurst:
        //                Job.WithCode(() =>
        //                {
        //                    for (int i = 0; i < entities.Length; i++)
        //                    {
        //                        destructionECB.DestroyEntity(entities[i]);
        //                    }
        //                }).Run();
        //                destructionECB.Playback(EntityManager);
        //                break;
        //        }

        //        switch (combination.CreationType)
        //        {
        //            case CreationMethod.IndividualArchetype:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    EntityManager.CreateEntity(archetype);
        //                }
        //                break;
        //            case CreationMethod.NativeArrayArchetype:
        //                EntityManager.CreateEntity(archetype, entities);
        //                break;
        //            case CreationMethod.CreateAndSetComponents:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    var entity = EntityManager.CreateEntity();
        //                    EntityManager.AddComponent<EcsTestData>(entity);
        //                    EntityManager.AddComponent<EntityEvent>(entity);
        //                }
        //                break;
        //            case CreationMethod.ECB_NativeArrayArchetype:
        //                creationECB.CreateEntity(archetype);
        //                creationECB.Playback(EntityManager);
        //                break;
        //            case CreationMethod.ECB_Individual:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    creationECB.CreateEntity(archetype);
        //                }
        //                creationECB.Playback(EntityManager);
        //                break;
        //            case CreationMethod.ECB_IndividualBurstQueued:
        //                Job.WithCode(() =>
        //                {
        //                    for (int i = 0; i < entities.Length; i++)
        //                    {
        //                        creationECB.CreateEntity(archetype);
        //                    }
        //                }).Run();
        //                creationECB.Playback(EntityManager);
        //                break;
        //        }
        //    }
        //}

        //[DisableAutoCreation]
        //public class EventComparisonTestSystem : SystemBase
        //{
        //    public EntityCommandBuffer ECB;
        //    protected override void OnCreate()
        //    {
        //        Group = new SampleGroupDefinition
        //        {
        //            AggregationType = AggregationType.Average,
        //            Name = "EntityCommandBuffer QueuedFromJob Destroyed Individually Inside Burst",
        //            SampleUnit = SampleUnit.Millisecond
        //        };

        //        StopWatch = new Stopwatch();
        //    }

        //    public int Measurements;
        //    public int Warmups;
        //    public int EventsPerArchetype;
        //    public NativeList<Entity> CreatedEntities;
        //    public SampleGroupDefinition Group;
        //    public EntityQuery Query;
        //    public EntityArchetype Archetype;
        //    public Stopwatch StopWatch;

        //    public enum CreationMethod
        //    {
        //        None = 0,
        //        IndividualArchetype,
        //        NativeArrayArchetype,
        //        CreateAndSetComponents,
        //        ECB_NativeArrayArchetype,
        //        ECB_Individual,
        //        ECB_IndividualBurst,
        //        ECB_IndividualBurstQueued,
        //    }

        //    public enum DestructionMethod
        //    {
        //        None = 0,
        //        IndividualEntity,
        //        WithQuery,
        //        WithNativeArray,
        //        ECB_WithQuery,
        //        ECB_Individual,
        //        ECB_IndividualBurst,
        //    }

        //    public struct Combination
        //    {
        //        public CreationMethod CreationType;
        //        public DestructionMethod DestructionType;
        //    }

        //    protected override void OnUpdate()
        //    {
        //        // Unable to compare all combinations with ECS events because delete is handled differently.
        //        // so its not a fair comparison including the switch statements.
        //        CompareDefaultMethods();

        //        this.ECB_BurstQueued_ECBQueryDestroyed();
        //        this.ECB_BurstQueued_EMQueryDestroyed();
        //        this.ECB_BurstQueued_IndividuallyDestroyed();
        //        this.EM_BatchArchetypeCreate_QueryDestroyed();
        //        this.EM_IndividualArchetypeCreate_QueryDestroyed();
        //    }

        //    public void CompareDefaultMethods()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        var creationMethods = Enum.GetValues(typeof(CreationMethod)).Cast<CreationMethod>().Skip(1);
        //        var destructionMethods = Enum.GetValues(typeof(DestructionMethod)).Cast<DestructionMethod>().Skip(1);
        //        var combinations = creationMethods.SelectMany(a => destructionMethods.Select(b => new Combination {
        //             CreationType = a,
        //             DestructionType = b, 
        //        })).ToList();

        //        foreach(var combination in combinations)
        //        {
        //            Group.Name = $"Create: {combination.CreationType}, Destroy: {combination.DestructionType}";

        //            EntityManager.DestroyEntity(Query);

        //            for (int i = 0; i < Measurements; i++)
        //            {
        //                entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));
        //                EntityManager.CreateEntity(archetype, entities);

        //                // Seems to crash he editor if you create and delete from the same ECB.
        //                var creationECB = new EntityCommandBuffer(Allocator.Temp);
        //                var destructionECB = new EntityCommandBuffer(Allocator.Temp);

        //                if (i < Warmups)
        //                {
        //                    Test(combination, entities, archetype, Query, creationECB, destructionECB);
        //                }
        //                else
        //                {
        //                    StopWatch.Restart();
        //                    Test(combination, entities, archetype, Query, creationECB, destructionECB);
        //                    StopWatch.Stop();
        //                    Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //                }
        //            }
        //        }
        //    }

        //    private void Test(Combination combination, NativeList<Entity> entities, EntityArchetype archetype, EntityQuery query, EntityCommandBuffer creationECB, EntityCommandBuffer destructionECB)
        //    {
        //        switch (combination.DestructionType)
        //        {
        //            case DestructionMethod.IndividualEntity:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    EntityManager.DestroyEntity(entities[i]);
        //                }
        //                break;
        //            case DestructionMethod.WithQuery:
        //                EntityManager.DestroyEntity(query);
        //                break;
        //            case DestructionMethod.WithNativeArray:
        //                EntityManager.DestroyEntity(entities);
        //                break;
        //            case DestructionMethod.ECB_WithQuery:
        //                destructionECB.DestroyEntity(query);
        //                destructionECB.Playback(EntityManager);
        //                break;
        //            case DestructionMethod.ECB_Individual:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    destructionECB.DestroyEntity(entities[i]);
        //                }
        //                destructionECB.Playback(EntityManager);
        //                break;
        //            case DestructionMethod.ECB_IndividualBurst:
        //                Job.WithCode(() =>
        //                {
        //                    for (int i = 0; i < entities.Length; i++)
        //                    {
        //                        destructionECB.DestroyEntity(entities[i]);
        //                    }
        //                }).Run();
        //                destructionECB.Playback(EntityManager);
        //                break;
        //        }

        //        switch (combination.CreationType)
        //        {
        //            case CreationMethod.IndividualArchetype:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    EntityManager.CreateEntity(archetype);
        //                }
        //                break;
        //            case CreationMethod.NativeArrayArchetype:
        //                EntityManager.CreateEntity(archetype, entities);
        //                break;
        //            case CreationMethod.CreateAndSetComponents:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    var entity = EntityManager.CreateEntity();
        //                    EntityManager.AddComponent<EcsTestData>(entity);
        //                    EntityManager.AddComponent<EntityEvent>(entity);
        //                }
        //                break;
        //            case CreationMethod.ECB_NativeArrayArchetype:
        //                creationECB.CreateEntity(archetype);
        //                creationECB.Playback(EntityManager);
        //                break;
        //            case CreationMethod.ECB_Individual:
        //                for (int i = 0; i < entities.Length; i++)
        //                {
        //                    creationECB.CreateEntity(archetype);
        //                }
        //                creationECB.Playback(EntityManager);
        //                break;
        //            case CreationMethod.ECB_IndividualBurstQueued:
        //                Job.WithCode(() =>
        //                {
        //                    for (int i = 0; i < entities.Length; i++)
        //                    {
        //                        creationECB.CreateEntity(archetype);
        //                    }
        //                }).Run();
        //                creationECB.Playback(EntityManager);
        //                break;
        //        }
        //    }

        //    public void ECB_BurstQueued_IndividuallyDestroyed()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        Group.Name = nameof(ECB_BurstQueued_IndividuallyDestroyed);
        //        EntityManager.DestroyEntity(Query);

        //        for (int i = 0; i < Measurements; i++)
        //        {
        //            entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

        //            if (i < Warmups)
        //            {

        //            }
        //            else
        //            {
        //                var ecb1 = new EntityCommandBuffer(Allocator.Temp);
        //                var ecb2 = new EntityCommandBuffer(Allocator.Temp);

        //                // If both use the same ECB the editor is crashing.

        //                StopWatch.Restart();

        //                Job.WithCode(() =>
        //                {
        //                    for (int j = 0; j < entities.Length; j++)
        //                    {
        //                        ecb1.DestroyEntity(entities[j]);
        //                    }

        //                    for (int j = 0; j < entities.Length; j++)
        //                    {
        //                        ecb2.CreateEntity(archetype);
        //                    }

        //                }).Run();

        //                ecb1.Playback(EntityManager);
        //                ecb2.Playback(EntityManager);
        //                StopWatch.Stop();
        //                Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //            }
        //        }
        //    }

        //    public void ECB_BurstQueued_EMQueryDestroyed()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        Group.Name = nameof(ECB_BurstQueued_EMQueryDestroyed);
        //        EntityManager.DestroyEntity(Query);

        //        for (int i = 0; i < Measurements; i++)
        //        {
        //            entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

        //            if (i < Warmups)
        //            {

        //            }
        //            else
        //            {
        //                var ecb1 = new EntityCommandBuffer(Allocator.Temp);

        //                StopWatch.Restart();
        //                EntityManager.DestroyEntity(Query);

        //                Job.WithCode(() =>
        //                {
        //                    for (int j = 0; j < entities.Length; j++)
        //                    {
        //                        ecb1.CreateEntity(archetype);
        //                    }

        //                }).Run();

        //                ecb1.Playback(EntityManager);
        //                StopWatch.Stop();
        //                Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //            }
        //        }
        //    }

        //    public void EM_BatchArchetypeCreate_QueryDestroyed()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        Group.Name = nameof(EM_BatchArchetypeCreate_QueryDestroyed);
        //        EntityManager.DestroyEntity(Query);

        //        for (int i = 0; i < Measurements; i++)
        //        {
        //            entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

        //            if (i < Warmups)
        //            {

        //            }
        //            else
        //            {

        //                StopWatch.Restart();
        //                EntityManager.DestroyEntity(Query);
        //                EntityManager.CreateEntity(archetype, entities);
        //                StopWatch.Stop();
        //                Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //            }
        //        }
        //    }

        //    public void EM_IndividualArchetypeCreate_QueryDestroyed()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        Group.Name = nameof(EM_BatchArchetypeCreate_QueryDestroyed);
        //        EntityManager.DestroyEntity(Query);

        //        for (int i = 0; i < Measurements; i++)
        //        {
        //            entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

        //            if (i < Warmups)
        //            {

        //            }
        //            else
        //            {

        //                StopWatch.Restart();
        //                EntityManager.DestroyEntity(Query);
        //                for (int j = 0; j < entities.Length; j++)
        //                {
        //                    EntityManager.CreateEntity(archetype);
        //                }
        //                EntityManager.CreateEntity(archetype, entities);
        //                StopWatch.Stop();
        //                Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //            }
        //        }
        //    }

        //    public void ECB_BurstQueued_ECBQueryDestroyed()
        //    {
        //        var entities = CreatedEntities;
        //        var archetype = Archetype;

        //        Group.Name =  nameof(ECB_BurstQueued_ECBQueryDestroyed);
        //        EntityManager.DestroyEntity(Query);

        //        for (int i = 0; i < Measurements; i++)
        //        {
        //            entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

        //            if (i < Warmups)
        //            {

        //            }
        //            else
        //            {
        //                var ecb1 = new EntityCommandBuffer(Allocator.Temp);

        //                StopWatch.Restart();
        //                ecb1.DestroyEntity(Query);

        //                Job.WithCode(() =>
        //                {
        //                    for (int j = 0; j < entities.Length; j++)
        //                    {
        //                        ecb1.CreateEntity(archetype);
        //                    }

        //                }).Run();

        //                ecb1.Playback(EntityManager);
        //                StopWatch.Stop();
        //                Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
        //            }
        //        }
        //    }
        //}

        [DisableAutoCreation]
        public class BurstECBEventQueueSystem : SystemBase
        {
            protected override void OnCreate()
            {
                Group = new SampleGroup("EntityCommandBuffer QueuedFromJob", SampleUnit.Millisecond);

                StopWatch = new Stopwatch();
            }

            public int Measurements { get; set; }

            public int Warmups { get; set; }

            public int EventsPerArchetype { get; set; }

            public NativeList<Entity> CreatedEntities { get; set; }

            public SampleGroup Group { get; private set; }

            public EntityQuery Query { get; set; }

            public EntityArchetype Archetype { get; set; }

            public Stopwatch StopWatch { get; private set; }

            protected override void OnUpdate()
            {
                var entities = CreatedEntities;
                var archetype = Archetype;

                for (int i = 0; i < Measurements; i++)
                {
                    entities.ResizeUninitialized(UnityEngine.Random.Range(1, EventsPerArchetype));

                    if (i < Warmups)
                    {

                    }
                    else
                    {
                        var ecb = new EntityCommandBuffer(Allocator.Temp);
                        StopWatch.Restart();
                        EntityManager.DestroyEntity(Query);

                        Job.WithCode(() =>
                        {
                            for (int j = 0; j < entities.Length; j++)
                            {
                                ecb.CreateEntity(archetype);
                            }

                        }).Run();

                        ecb.Playback(EntityManager);
                        StopWatch.Stop();
                        Measure.Custom(Group, StopWatch.Elapsed.TotalMilliseconds);
                    }
                }
            }
        }

        /*[Test, Performance, TestCategory(TestCategory.Performance)]
        public void UpdatePhaseBreakdown([Values(1, 1000, 10000)] int eventsPerArchetype, [Values(1, 25, 50)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerArchetype;
            system.ArchetypeCount = archetypeCount;


            var processQueuedEventsGroup = new SampleGroup(GetName("ProcessQueuedEvents", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);
            var structuralChangesEventsGroup = new SampleGroup(GetName("StructuralChanges", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);
            var updateChunkCollectionsGroup = new SampleGroup(GetName("UpdateChunkCollections", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);
            var setComponentsGroup = new SampleGroup(GetName("SetComponents", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);
            var clearQueuesGroup = new SampleGroup(GetName("ClearQueues", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.ProcessQueuedEvents();
                    sw.Stop();
                    Measure.Custom(processQueuedEventsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.Data.StructuralChanges.Apply();
                    Measure.Custom(structuralChangesEventsGroup, sw.Elapsed.TotalMilliseconds);
                    sw.Stop();

                    sw.Restart();
                    EventSystem.UpdateChunkCollections();
                    sw.Stop();
                    Measure.Custom(updateChunkCollectionsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.SetComponents();
                    sw.Stop();
                    Measure.Custom(setComponentsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.ClearQueues();
                    sw.Stop();
                    Measure.Custom(clearQueuesGroup, sw.Elapsed.TotalMilliseconds);
                }
            }
        }*/

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateEvents([Values(1, 10, 100, 1000, 10000)] int eventsPerArchetype, [Values(1, 10, 50, 100)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerArchetype;
            system.ArchetypeCount = archetypeCount;

            var group = new SampleGroup(GetName("Creating events that were queued on main thread", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if(i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }

            system.IsParallel = true;
            system.ThreadsPerArchetype = 4;

            // It matters whether the events are queued in a single thread or multiple 
            // because of the way data is stored and read. It will effect set component time, 
            // counting how many entities are queued, etc.

            var group2 = new SampleGroup(GetName("Creating events that were queued in parallel", eventsPerArchetype, archetypeCount), SampleUnit.Millisecond);

            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group2, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateBufferEvents([Values(1, 10, 100, 1000)] int eventCount, [Values(1, 10, 100, 1000)] int bufferLength)
        {
            var system = Manager.World.GetOrCreateSystem<BufferEventFromJobsWithCodeSystem>();

            system.EventCount = eventCount;
            system.BufferElementCount = bufferLength;

            var group = new SampleGroup(GetName($"Creating {eventCount} events with {bufferLength} length buffers, queued from main thread "), SampleUnit.Millisecond);

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }
        }


        [DisableAutoCreation]
        public class BufferEventFromJobsWithCodeSystem : SystemBase
        {
            private EventQueue<EcsTestData, EcsIntElement> _queue;

            public int EventCount { get; internal set; } = 1;

            public int BufferElementCount { get; internal set; } = 1;

            protected override void OnCreate()
            {
                _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData, EcsIntElement>();
            }

            protected unsafe override void OnUpdate()
            {
                var queue = _queue;
                var componentCount = EventCount;
                var bufferElementCount = BufferElementCount;

                if (bufferElementCount <= 0)
                    throw new ArgumentException();

                var bufferPtr = new NativeArray<EcsIntElement>(bufferElementCount, Allocator.Temp).GetUnsafePtr();

                Job.WithCode(() =>
                {
                    for (int i = 0; i < componentCount; i++)
                    {
                        queue.Enqueue(EventComponentData, bufferPtr, bufferElementCount);
                    }

                }).Run();
            }
        }

        private string GetName([CallerMemberName] string memberName = "Method", int eventsPerArchetype = 0, int archetypeCount = 0)
        {
            string parenStr = "";
            if (eventsPerArchetype > 0 && archetypeCount > 0)
            {
                parenStr = $" (EventsPerAchetype={eventsPerArchetype}, Archetypes={archetypeCount} Total={eventsPerArchetype * archetypeCount})";
            }
            return $"{memberName}{parenStr}";
        }

        [DisableAutoCreation]
        public class LoadTestSystem : SystemBase
        {
            private EventQueue _queue;

            private static NativeArray<TypeManager.TypeInfo> _componentTypeInfos;
            private UnityEngine.Random _random;
            private int archetypeCount = 1;
            private EntityEventSystem _eventSystem;

            public bool IsCreated { get; private set; }

            public int EventsPerArchetype { get; set; } = 1;

            public bool IsParallel { get; set; } = false;

            public int ThreadsPerArchetype { get; set; } = 1;


            public NativeList<EventQueue> _queues;

            public int ArchetypeCount
            {
                get => archetypeCount; internal
                set
                {
                    if (!IsCreated)
                        throw new InvalidOperationException();

                    archetypeCount = value;

                    // Pick a random block of IComponentData
                    _queues.Clear();

                    for (int i = 0; i < value; i++)
                    {
                        //var idx = _random.Next(0, _componentTypeInfos.Length - 1);
                        var info = _componentTypeInfos[math.min(i, _componentTypeInfos.Length - 1)];
                        var queue = _eventSystem.GetQueue(info);
                        _queues.Add(queue);
                    }
                }
            }

            protected override void OnCreate()
            {
                _eventSystem = World.GetOrCreateSystem<EntityEventSystem>();
                _queues = new NativeList<EventQueue>(Allocator.Persistent);

                if (!_componentTypeInfos.IsCreated)
                {
                    // Store all the component types so that we can simiulate events with them.
                    // * Avoid ChunkHeader - EntityManagerDebug.CheckInternalConsistency() will fail tests if ChunkHeader component is used.
                    // * Avoid all test data components or there's a chance to get 2x on the same entity.

                    var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();

                    var types = TypeManager.GetAllTypes().Where(t => t.SizeInChunk > 1 && t.SizeInChunk < 128 && t.Category == TypeManager.TypeCategory.ComponentData
                        && !t.Type.FullName.Contains(nameof(Vella.Events)) && t.TypeIndex != chunkHeaderTypeIndex);

                    _componentTypeInfos = new NativeArray<TypeManager.TypeInfo>(types.ToArray(), Allocator.Persistent);
                }

                _random = new UnityEngine.Random();

                IsCreated = true;
            }

            protected override void OnDestroy()
            {
                _componentTypeInfos.Dispose();
                _queues.Dispose();
            }

            protected unsafe override void OnUpdate()
            {
                var queues = _queues;
                var count = EventsPerArchetype;

                if (!IsParallel)
                {
                    Job.WithCode(() =>
                    {
                        for (int i = 0; i < queues.Length; i++)
                        {
                            var queue = queues[i];

                            for (int j = 0; j < count; j++)
                            {
                                queue.EnqueueDefault();
                            }
                        }

                    }).Run();
                }
                else if (ThreadsPerArchetype > 0 && ThreadsPerArchetype < JobsUtility.MaxJobThreadCount)
                {
                    var numPerThread = EventsPerArchetype / ThreadsPerArchetype;
                    var handles = new NativeArray<JobHandle>(queues.Length, Allocator.Temp);

                    for (int i = 0; i < queues.Length; i++)
                    {
                        handles[i] = new ThreadedJob
                        {
                            Events = _queues[i],

                        }.Schedule(ThreadsPerArchetype, numPerThread);
                    }

                    JobHandle.CombineDependencies(handles).Complete();
                }
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct ThreadedJob : IJobParallelFor
            {
                public EventQueue Events;

                public void Execute(int index)
                {
                    Events.EnqueueDefault();
                }
            }
        }
    }
}

