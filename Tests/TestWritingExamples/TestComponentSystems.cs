//using Unity.Jobs;

//namespace Vella.Events.Tests
//{
//    [AlwaysUpdateSystem]
//    public class TestEcsChangeSystem : JobComponentSystem
//    {
//        public int NumChanged;
//        EntityQuery ChangeGroup;
//        protected override void OnCreate()
//        {
//            ChangeGroup = GetEntityQuery(typeof(EcsTestData));
//            ChangeGroup.SetChangedVersionFilter(typeof(EcsTestData));
//        }

//        protected override JobHandle OnUpdate(JobHandle inputDeps)
//        {
//            NumChanged = ChangeGroup.CalculateEntityCount();
//            return inputDeps;
//        }
//    }
//}
