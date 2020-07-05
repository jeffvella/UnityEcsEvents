
# UnityEcsEvents
An event system package for Unity's data oriented design framework.

#### What is it?

The concept of Entity Events in Entity Component Systems (ECS) is fairly common. Used mostly for short-lived/one frame messaging, its a convenient way of triggering functionality. In Unity this can be accomplished easily by creating an entity and assigning components to it. 

#### Then why do i need a fancy package?

This has many optimizations that allow for creating events faster than you could otherwise. It also has many additional features such as parallel support, attaching DynamicBuffers to events!

[Check out the example project here](https://github.com/jeffvella/UnityEcsEvents.Example)

### Installation:

- **Option 1: Download**  
Download by clicking the "Clone or Download" button on the GitHub repo and copy the contents to your unity project's /packages/ folder.

- **Option 2: PackageManager**  
Click the on the GitHub repo, and copy the URL shown. In PackageManager hit the [+] icon,  select [Add Package from Git URL] and then paste in the URL, [Add]. You will need [Clone or Download] button [Git](https://git-scm.com/ "Git") installed on your machine for this to work. for more info see the  [PackageManager docs](https://docs.unity3d.com/Manual/upm-ui-giturl.html "PackageManager docs"). After clicking [Add] it can take 30 seconds or so before it looks like its doing anything.

### Package Dependencies:

    "com.unity.entities": "0.8.0-preview.8",
    "com.unity.burst": "1.3.0-preview.7",
    "com.unity.collections": "0.7.0-preview.2",
    "com.unity.test-framework": "1.1.11",
    "com.unity.test-framework.performance": "1.3.3-preview",
    
### Supported Editors:

  * 2019.3.6xx+
 
### Acknowledgements

Consider checking out the project that inspired this work here:  **[com.bovinelabs.entities](https://github.com/tertle/com.bovinelabs.entities)** and the Unity forums thread on event systems: [here](https://forum.unity.com/threads/event-system.779711/#post-5677585).
