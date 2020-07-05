
# UnityEcsEvents
An event system package for Unity's data oriented design framework.

#### What is it?

Events in ECS are a convienient way to communicate short-lived information between systems. An event is just an Entity with a few components on it.

[Check out the example project here](https://github.com/jeffvella/UnityEcsEvents.Example)

### Installation:

Download by clicking the "Clone or Download" button on the GitHub repo then copy the folders into your "/packages/" folder.

There are two seperate packages included:
- Entities.Unlocked - Provides access to various internal features of Unity.Entities
- UnityEcsEvents - The core events system.

### Package Dependencies:

    "com.unity.burst": "1.3.3",
    "com.unity.collections": "0.9.0-preview.6",
    "com.unity.entities": "0.11.1-preview.4",
	"com.unity.test-framework": "1.1.11",
    "com.unity.test-framework.performance": "1.3.3-preview",
	"com.vella.entities.unlocked": "0.0.3"

### Supported Editors:

  * 2020.1.0b14+
 
### Acknowledgements

Consider checking out the project that inspired this work here:  **[com.bovinelabs.entities](https://github.com/tertle/com.bovinelabs.entities)** and the Unity forums thread on event systems: [here](https://forum.unity.com/threads/event-system.779711/#post-5677585).
