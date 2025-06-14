# JPOGRaptor 🦖

This repository contains the full source code for the JPOGRaptor for Lethal Company, including the Unity project which can be used to build its asset bundle.  
The guide I followed for this project can be found [here](https://lethal.wiki/dev/apis/lethallib/custom-enemies/overview.).  
A video Tutorial can be found [here](https://www.youtube.com/watch?v=NZ_F8wDczzM).

 > [!NOTE]  
 > The wiki page might be slightly out of date regarding how the JPOGRaptor prefab is constructed.

> [!NOTE]  
> I do not own any of the Assets nor made them myself. The models, animations and some of the audio clips were made by: [Blue Tongue Entertainment](https://en.wikipedia.org/wiki/Blue_Tongue_Entertainment), which was closed down in 2011.

## Dependencies ✅

You need to install the following dependencies for this mod to work in the game (these are not installed by the setup script):

- [LethalLib](https://thunderstore.io/c/lethal-company/p/Evaisa/LethalLib/) for registering and adding our enemy.
    - LethalLib depends on [HookGenPatcher](https://thunderstore.io/c/lethal-company/p/Evaisa/HookGenPatcher/).

If you didn't run the setup script you will also need to, in the `Plugin` directory where our plugin code is, run `dotnet tool restore` on the command-line to install the rest of the dependencies.  
That should be all the required setup for this project, and now you can move to coding AI or making your own 3D models for your custom enemy. Good luck!

### Thunderstore Packages 📦

We have configured [JPOGRaptor.csproj](/Plugin/JPOGRaptor.csproj) to build a Thunderstore package to [/Plugin/Thunderstore/Packages/](/Plugin/Thunderstore/Packages/) using [tcli](https://github.com/thunderstore-io/thunderstore-cli/wiki) each time we make a release build of our mod. A release build can be done for example from the command-line like this: `dotnet build -c release`. This will use configuration options from [thunderstore.toml](/Plugin/Thunderstore/thunderstore.toml), so configure it for your own mod! Do note that we have not included a way to upload your mod to thunderstore via tcli in this project.

## Credits 🗣️

- [EvaisaDev](https://github.com/EvaisaDev) - [LethalLib](https://github.com/EvaisaDev/LethalLib)  
- [Lordfirespeed](https://github.com/Lordfirespeed) - reference tcli usage in LethalLib  
- [Xilophor](https://github.com/Xilophor) - csproj files taken from Xilo's [mod templates](https://github.com/Xilophor/Lethal-Company-Mod-Templates)  
- [XuuXiao](https://github.com/XuuXiao/) - porting LC-ExampleEnemy for LC v50  
- [nomnomab](https://github.com/nomnomab) - [Lethal Company Project Patcher](https://github.com/nomnomab/lc-project-patcher) - used for the Unity Project
- [HENDRIX-ZT2 ](https://github.com/HENDRIX-ZT2) & [AdventureT](https://github.com/AdventureT) - creating the blender plugin: [jpog-blender](https://github.com/HENDRIX-ZT2/jpog-blender) that is able to read ".tmd" files from the game: [Jurassic Park: Operation Genisis](https://en.wikipedia.org/wiki/Jurassic_Park:_Operation_Genesis)