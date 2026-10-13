# DMD player plugin

Add this optional package to the player application together with `org.visualpinball.engine.dmd`. The adapter owns configuration and resource subscriptions; legacy automatic bridges stay inactive in a host-managed player.

Settings → Plugins → DMD provides a global altcolor library, cabinet device setup, native-window placement and appearance. Selected game can choose a colorization version and override supported display options. The library contains a folder per ROM; variant subfolders can hold Serum or PAC files, palettes, and paired VNI/FSQ animations. The selector offers only discovered assets, retains missing explicit selections, and blocks ambiguous Automatic selection until a version is chosen. PAC assets require the configured key.

LibDmd receives an owned cache containing exactly the chosen files under its canonical filenames. Original colorization files remain unchanged. The cache and physical output reservations are released after the worker has stopped; a blocked worker keeps its reservation until deferred cleanup finishes. INI import is an explicit draft action: review the imported settings and Apply. The original INI is never rewritten.

The adapter binds original display frames from the selected game's controller, negotiates raw identification when colorization requires it, and publishes colorization plus scaling as one derived presentation resource. PinUp and other identification consumers keep the original stream. Controller-relative source selections remain usable when configuring a stopped game.

Native backends and cabinet devices depend on the base DMD package and its platform libraries. The portable catalog tests validate discovery, ambiguity and staging independently of Unity or hardware; real destinations require target-platform testing.
