
# UnityEcsEvents
An event system package for Unity's data-oriented design framework.

#### What is it?

Events in ECS are a convenient way to communicate short-lived information between systems. An event is just an Entity with a few components on it and this project makes it faster and easier to create them!

[Check out the example project here](https://github.com/jeffvella/UnityEcsEvents.Example)

### Installation:

Download by clicking the "Clone or Download" button on the GitHub repo then copy the folders into your "/packages/" folder.

There are two separate packages included:
- Entities.Unlocked - Provides access to various internal features of Unity.Entities
- UnityEcsEvents - The core events system.

### Getting Started:

Here's an example of routing ECS events through to a MonoBehavior:

    using Unity.Collections;
    using Unity.Entities;
    using UnityEngine;
    using Vella.Events;

    public class HelloWorld : MonoBehaviour, IEventObserver<MyHelloEvent>
    {
        public EventRouter EventSource;

        private void Start() => EventSource.Subscribe<MyHelloEvent>(this);

        public void OnEvent(MyHelloEvent e)
        {
            Debug.Log($"Event Triggered in GameObject. Message={e.Message}, Value={e.SomeValue}");
        }
    }

    public class TestSystem : SystemBase
    {
        private EntityEventSystem _eventSystem;

        protected override void OnCreate()
        {
            _eventSystem = World.GetOrCreateSystem<EntityEventSystem>();

            _eventSystem.Enqueue(new MyHelloEvent
            {
                Message = "Hello World",
                SomeValue = 41
            });
        }

        protected override void OnUpdate()
        {
            Entities.ForEach((in MyHelloEvent e) =>
            {
                Debug.Log($"Event Triggered in System. Message={e.Message}, Value={e.SomeValue}");

            }).Run();
        }
    }

    public struct MyHelloEvent : IComponentData
    {
        public NativeString64 Message;
        public int SomeValue;
    }


In general you can also:
 * Create events anywhere with the same syntax - ForEach, Jobs, Managed, in parallel.
 * Attach DynamicBuffers and Arrays to events.
 * Cache and re-use an event queue per system.

[For more advanced examples have a look at the test project](https://github.com/jeffvella/UnityEcsEvents.Example)

### Package Dependencies:

    "com.unity.burst": "1.3.3",
    "com.unity.collections": "0.9.0-preview.6",
    "com.unity.entities": "0.11.1-preview.4",
	"com.unity.test-framework": "1.1.11",
    "com.unity.test-framework.performance": "1.3.3-preview",
	"com.vella.entities.unlocked": "0.0.3"

### Supported Editors:

  * 2020.1.0b14+
 
### Troubleshooting

 * Unity still has some issues with ASMREF in certain situations, if Unity.Entities doesnt have the Unlocked package compiled with it properly then the usual fix is to go into preferences >> External tools and click 'regenerate project files'.
 
### Acknowledgements

Consider checking out the project that inspired this work here:  **[com.bovinelabs.entities](https://github.com/tertle/com.bovinelabs.entities)** and the Unity forums thread on event systems: [here](https://forum.unity.com/threads/event-system.779711/#post-5677585).
