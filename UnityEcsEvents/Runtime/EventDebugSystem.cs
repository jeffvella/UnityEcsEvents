using Unity.Entities;
using Vella.Events;

#if UNITY_EDITOR

namespace Assets.Scripts.Systems
{
    /// <summary>
    /// Adds friendly names to Events for easier viewing in the EntityDebugger
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public class EventDebugSystem : SystemBase
    {
        private EntityQuery _allEvents;

        protected override void OnUpdate()
        {
            Entities.ForEach((Entity entity, in EntityEvent eventInfo) =>
            {
                string name = TypeManager.GetType(eventInfo.ComponentTypeIndex).Name;
                EntityManager.SetName(entity, $"{entity}, {name}: {eventInfo.Id}");

            }).WithStructuralChanges().WithStoreEntityQueryInField(ref _allEvents).Run();
        }
    }
}

#endif
