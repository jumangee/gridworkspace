# Space Engineers API dump

Generated: 2026-11-18 22:46  
Source assemblies: `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`  
Types: 44

## `Sandbox.Definitions.MyCubeBlockDefinition`

- assembly: `Sandbox.Game`
- kind: class 
- base: `Sandbox.Definitions.MyPhysicalModelDefinition`

### Constructors
```csharp
public MyCubeBlockDefinition()
```

### Fields
```csharp
public MyCubeSize CubeSize
public MyBlockTopology BlockTopology
public Vector3I Size
public Vector3 ModelOffset
public Boolean UseModelIntersection
public MyCubeDefinition CubeDefinition
public Boolean SilenceableByShipSoundSystem
public Boolean AllowInteractionThroughBlock
public Boolean YesNoToolbarBackground
public Component[] Components
public UInt16 CriticalGroup
public Single CriticalIntegrityRatio
public Single OwnershipIntegrityRatio
public Single MaxIntegrityRatio
public Single MaxIntegrity
public Nullable<Int32> DamageEffectID
public String DamageEffectName
public Nullable<Vector3> DamageEffectOffset
public Nullable<Vector3> AimingOffset
public String DestroyEffect
public Nullable<Vector3> DestroyEffectOffset
public MySoundPair DestroySound
public CubeBlockEffectBase[] Effects
public Single DamageMultiplierExplosion
public Single DamageThreshold
public Single DetonateChance
public String AmmoExplosionEffect
public String AmmoExplosionSound
public MountPoint[] MountPoints
public Dictionary<Vector3I, Dictionary<Vector3I, MyCubePressurizationMark>> IsCubePressurized
public Color Color
public List<MyCubeBlockDefinition> Variants
public MyCubeBlockDefinition UniqueVersion
public MyPhysicsOption PhysicsOption
public Nullable<MyStringId> DisplayNameVariant
public String BlockPairName
public Boolean UsesDeformation
public Single DeformationRatio
public Single IntegrityPointsPerSec
public String EdgeType
public List<BoneInfo> Skeleton
public Dictionary<Vector3I, Vector3> Bones
public Nullable<Boolean> IsAirTight
public Boolean IsStandAlone
public Boolean HasPhysics
public Boolean UseNeighbourOxygenRooms
public MyStringId BuildType
public String BuildMaterial
public String YesNoToolbarYesTooltip
public String YesNoToolbarNoTooltip
public MyDefinitionId[] GeneratedBlockDefinitions
public MyStringId GeneratedBlockType
public Single BuildProgressToPlaceGeneratedBlocks
public MyStringHash EmissiveColorPreset
public MyDefinitionId[] BlockStages
public BuildProgressModel[] BuildProgressModels
public Single DisassembleRatio
public MyAutorotateMode AutorotateMode
public MySoundPair PrimarySound
public MySoundPair ActionSound
public MySoundPair DamagedSound
public Int32 PCU
public Boolean PlaceDecals
public Nullable<Vector3> DepressurizationEffectOffset
public List<UInt32> TieredUpdateTimes
public Dictionary<String, MyObjectBuilder_ComponentBase> EntityComponents
public Nullable<VoxelPlacementOverride> VoxelPlacement
public Nullable<VoxelPlacementSettings> SingleBlockGridVoxelPlacement
public Single GeneralDamageMultiplier
public MyBlockVariantGroup BlockVariantsGroup
public Single DestroyEffectScale
public Boolean EnableUseObjectSimpleTargeting
public Nullable<Vector3> MechanicalTopInitialPlacementOffset
public WheelPlacementColliderSettings WheelPlacementPlacementCollider
public MyCubeBlockTagDefinition TagDefinition
public static readonly Int32 PCU_CONSTRUCTION_STAGE_COST
```

### Properties
```csharp
public MyBlockDirection Direction { get; set; }
public MyBlockRotation Rotation { get; set; }
public Boolean IsGeneratedBlock { get; }
public Vector3I Center { get; }
public Vector3I MirroringCenter { get; }
public MySymmetryAxisEnum SymmetryX { get; }
public MySymmetryAxisEnum SymmetryY { get; }
public MySymmetryAxisEnum SymmetryZ { get; }
public String MirroringBlock { get; }
public String DisplayNameText { get; }
public Boolean GuiVisible { get; set; }
public Boolean Mirrored { get; set; }
public Boolean RandomRotation { get; set; }
public Boolean NewsletterSubscriptionNeeded { get; set; }
public Boolean SimpleUpdateVisualOnSkinChange { get; set; }
public Boolean IsInventoryItem { get; set; }
public Boolean Repairable { get; set; }
public List<MyStringHash> TargetingGroups { get; set; }
public Single PriorityModifier { get; set; }
public Boolean UseVanillaPlacementDetection { get; set; }
public Single NotWorkingPriorityMultiplier { get; set; }
```

### Methods
```csharp
public static Void ClearPreloadedConstructionModels()
public Void ClearTargetableCache()
public Boolean ContainsComputer()
public Single FinalModelThreshold()
public Int32 GetBuildProgressModelIndex(Single percentageA)
public MountPoint[] GetBuildProgressModelMountPoints(Single currentIntegrityRatio)
public MyCubeBlockDefinition GetGeneratedBlockDefinition(MyStringId additionalModelType)
public String GetLimitType()
public static Int32 GetMountPointWallIndex(Direction direction)
public virtual MyObjectBuilder_DefinitionBase GetObjectBuilder()
public Void InitPressurization()
public Boolean IsTurretTargetable()
public Boolean MatchingTurretTargetingGroup(MyStringHash hash)
public Boolean ModelChangeIsNeeded(Single was, Single now)
public Vector3 MountPointLocalNormalToBlockLocal(Vector3 normal, Direction mountPointDirection)
public Vector3 MountPointLocalToBlockLocal(Vector3 coord, Direction mountPointDirection)
public static BlockSideEnum NormalToBlockSide(Vector3I normal)
public static Void PreloadConstructionModels(MyCubeBlockDefinition block)
public Boolean RatioEnoughForDamageEffect(Single ratio)
public Boolean RatioEnoughForOwnership(Single ratio)
```

## `Sandbox.Definitions.MyDefinitionManager`

- assembly: `Sandbox.Game`
- kind: class 
- base: `VRage.Game.MyDefinitionManagerBase`

### Properties
```csharp
public static MyDefinitionManager Static { get; }
public Boolean Loading { get; set; }
public MyEnvironmentDefinition EnvironmentDefinition { get; }
public DictionaryValuesReader<String, MyCharacterDefinition> Characters { get; }
public MyShipSoundSystemDefinition GetShipSoundSystemDefinition { get; }
public Int32 VoxelMaterialCount { get; }
public Int32 VoxelMaterialRareCount { get; }
```

### Events
```csharp
public event Action<String> SBCLoaded
```

### Methods
```csharp
public Void AddMissingWheelModelDefinition(String wheelType)
public DictionaryValuesReader<MyDefinitionId, MyDefinitionBase> GetAllDefinitions()
public List<Tuple<MyObjectBuilder_Definitions, String>> GetAllSessionPreloadObjectBuilders()
public DictionaryReader<MyDefinitionId, MyStoryDefinition> GetAllStoriesDefinition(MyDefinitionId categoryDefinition)
public DictionaryReader<MyDefinitionId, MyStoryCategoryDefinition> GetAllStoryCategoriesDefinition(MyDefinitionId categoryDefinition)
public MyAmmoDefinition GetAmmoDefinition(MyDefinitionId id)
public MyAmmoMagazineDefinition GetAmmoMagazineDefinition(MyDefinitionId id)
public String GetAnimationDefinitionCompatibility(String animationSubtypeName)
public ListReader<MyAnimationDefinition> GetAnimationDefinitions()
public Dictionary<String, MyAnimationDefinition> GetAnimationDefinitions(String skeleton)
public MyAssetModifierDefinition GetAssetModifierDefinition(MyDefinitionId id)
public MyAssetModifiers GetAssetModifierDefinitionForRender(MyStringHash skinId)
public MyAssetModifiers GetAssetModifierDefinitionForRender(String skinId)
public DictionaryValuesReader<MyDefinitionId, MyAssetModifierDefinition> GetAssetModifierDefinitions()
public DictionaryReader<MyStringHash, MyAssetModifiers> GetAssetModifierDefinitionsForRender()
public DictionaryReader<String, MyAsteroidGeneratorDefinition> GetAsteroidGeneratorDefinitions()
public Void GetBaseBlockPrefabName(MyCubeSize size, Boolean isStatic, Boolean isCreative, out ref String prefabName)
public MyBehaviorDefinition GetBehaviorDefinition(MyDefinitionId id)
public ListReader<MyBehaviorDefinition> GetBehaviorDefinitions()
public DictionaryReader<String, MyBlockVariantGroup> GetBlockVariantGroupDefinitions()
public MyBlueprintClassDefinition GetBlueprintClass(String className)
public MyBlueprintDefinitionBase GetBlueprintDefinition(MyDefinitionId blueprintId)
public DictionaryValuesReader<MyDefinitionId, MyBlueprintDefinitionBase> GetBlueprintDefinitions()
public MyBotDefinition GetBotDefinition(MyDefinitionId id)
public ListReader<MyBotDefinition> GetBotDefinitions()
public Dictionary<String, MyGuiBlockCategoryDefinition> GetCategories()
public MyCubeBlockDefinition GetComponentBlockDefinition(MyDefinitionId componentDefId)
public MyComponentDefinition GetComponentDefinition(MyDefinitionId id)
public MyComponentGroupDefinition GetComponentGroup(MyDefinitionId groupDefId)
public MyDefinitionId GetComponentId(MyCubeBlockDefinition blockDefinition)
public MyDefinitionId GetComponentId(MyDefinitionId defId)
public ListReader<MyPhysicalItemDefinition> GetConsumableDefinitions()
public MyContainerDefinition GetContainerDefinition(MyDefinitionId containerId)
public MyContainerTypeDefinition GetContainerTypeDefinition(MyDefinitionId id)
public MyContainerTypeDefinition GetContainerTypeDefinition(String containerName)
public MyContractTypeDefinition GetContractType(String subtype)
public DictionaryReader<MyDefinitionId, MyContractTypeDefinition> GetContractTypeDefinitions()
public MyControllerSchemeDefinition GetControllerScheme(String subtype = General)
public ListReader<MyControllerSchemeDefinition> GetControllerSchemesAll()
public List<MyControllerSchemeDefinition> GetControllerSchemesSelectable()
public MyCubeBlockDefinition GetCubeBlockDefinition(MyDefinitionId id)
public MyCubeBlockDefinition GetCubeBlockDefinition(MyObjectBuilder_CubeBlock builder)
public Vector2I GetCubeBlockScreenPosition(MyCubeBlockDefinitionGroup blockDefinitionGroup)
public Single GetCubeSize(MyCubeSize gridSize)
public Single GetCubeSizeOriginal(MyCubeSize gridSize)
public ListReader<MyDebrisDefinition> GetDebrisDefinitions()
public DictionaryValuesReader<MyDefinitionId, MyDebugScreenSearchCacheDefinition> GetDebugScreenSearchCache()
public List<MyFactionDefinition> GetDefaultFactions()
public MyVoxelMaterialDefinition GetDefaultVoxelMaterialDefinition()
public Void GetDefinedEntityComponents(ref ref List<MyDefinitionId> definedComponents)
public Void GetDefinedEntityContainers(ref ref List<MyDefinitionId> definedContainers)
public MyDefinitionBase GetDefinition(MyDefinitionId id)
public MyCubeBlockDefinitionGroup GetDefinitionGroup(String groupName)
public DictionaryKeysReader<String, MyCubeBlockDefinitionGroup> GetDefinitionPairNames()
public Dictionary<String, MyCubeBlockDefinitionGroup> GetDefinitionPairs()
public ListReader<T> GetDefinitionsOfType<T>()
public MyDropContainerDefinition GetDropContainerDefinition(String id)
public DictionaryReader<String, MyDropContainerDefinition> GetDropContainerDefinitions()
public MyEdgesDefinition GetEdgesDefinition(MyDefinitionId id)
public ListReader<MyEdgesDefinition> GetEdgesDefinitions()
public IEnumerable<MyEmoteDefinition> GetEmoteDefinitions()
public MyComponentDefinitionBase GetEntityComponentDefinition(MyDefinitionId componentId)
public ListReader<MyComponentDefinitionBase> GetEntityComponentDefinitions()
public ListReader<T> GetEntityComponentDefinitions<T>()
public ListReader<MyEnvironmentItemsDefinition> GetEnvironmentItemClassDefinitions()
public MyEnvironmentItemDefinition GetEnvironmentItemDefinition(MyDefinitionId id)
public ListReader<MyEnvironmentItemDefinition> GetEnvironmentItemDefinitions()
public ListReader<MyDefinitionId> GetEnvironmentItemsDefinitions(Int32 channel)
public MyGlobalEventDefinition GetEventDefinition(MyDefinitionId id)
public MyFactionNameDefinition GetFactionName(String subtype)
public DictionaryReader<MyDefinitionId, MyFactionNameDefinition> GetFactionNameDefinitions()
public List<MyFactionDefinition> GetFactionsFromDefinition()
public ListReader<MyFactionStationAdvertDefinition> GetFactionStationAdvertDefinitions()
public String GetFirstRespawnShip()
public MyGridCreateToolDefinition GetGridCreator(MyStringHash name)
public IEnumerable<MyGridCreateToolDefinition> GetGridCreatorDefinitions()
public DictionaryValuesReader<String, MyGroupedIds> GetGroupedIds(String superGroup)
public MyComponentGroupDefinition GetGroupForComponent(MyDefinitionId componentDefId, out ref Int32 amount)
public DictionaryValuesReader<MyDefinitionId, MyHandItemDefinition> GetHandItemDefinitions()
public ListReader<MyDefinitionBase> GetInventoryItemDefinitions()
public ListReader<MyLCDTextureDefinition> GetLCDTexturesDefinitions()
public virtual MyDefinitionSet GetLoadingSet()
public MyLootBagDefinition GetLootBagDefinition(MyCubeSize size)
public DictionaryValuesReader<MyDefinitionId, MyMainMenuInventorySceneDefinition> GetMainMenuInventoryScenes()
public Nullable<Int32> GetMeteorEventAmountMax()
public Nullable<Int32> GetMeteorEventAmountMin()
public Nullable<Single> GetMeteorEventCataclysmAmountMultiplier()
public Nullable<Single> GetMeteorEventDisableRadius()
public Nullable<Single> GetMeteorEventInitialSpawnDistance()
public Nullable<Single> GetMeteorEventSpawnDistanceIncrement()
public Nullable<Single> GetMeteorEventTargetRadius()
public Nullable<Single> GetMeteorHealth()
public Nullable<Single> GetMeteorLootAmountMax()
public Nullable<Single> GetMeteorLootAmountMin()
public MyObjectBuilder_DefinitionBase GetObjectBuilder(MyDefinitionBase definition)
public MyOffensiveWordsDefinition GetOffensiveWordsForPlatform()
public Void GetOreTypeNames(out ref String[] outNames)
public MyPhysicalItemDefinition GetPhysicalItemDefinition(MyObjectBuilder_Base objectBuilder)
public MyPhysicalItemDefinition GetPhysicalItemDefinition(MyDefinitionId id)
public ListReader<MyPhysicalItemDefinition> GetPhysicalItemDefinitions()
public MyPhysicalItemDefinition GetPhysicalItemForHandItem(MyDefinitionId handItemId)
public MyPhysicalMaterialDefinition GetPhysicalMaterialDefinition(MyDefinitionId id)
public MyPhysicalMaterialDefinition GetPhysicalMaterialDefinition(String name)
public ListReader<MyPhysicalMaterialDefinition> GetPhysicalMaterialDefinitions()
public DictionaryValuesReader<MyDefinitionId, MyPirateAntennaDefinition> GetPirateAntennaDefinitions()
public IEnumerable<MyPlanetGeneratorDefinition> GetPlanetsGeneratorsDefinitions()
public DictionaryValuesReader<MyDefinitionId, MyPlanetPrefabDefinition> GetPlanetsPrefabsDefinitions()
public MyPrefabDefinition GetPrefabDefinition(String id)
public DictionaryReader<String, MyPrefabDefinition> GetPrefabDefinitions()
public HashSet<String> GetPreloadSet(PreloadType preloadType)
public MyRadialMenu GetRadialMenuDefinition(String subtype)
public String GetRandomCharacterName()
public MyEnvironmentItemsDefinition GetRandomEnvironmentClass(Int32 channel)
public MyStoryDefinition GetRandomStoryDefinition(MyDefinitionId categoryDefinition)
public MyResearchBlockDefinition GetResearchBlock(MyDefinitionId id)
public DictionaryValuesReader<MyDefinitionId, MyResearchBlockDefinition> GetResearchBlockDefinitions()
public MyResearchGroupDefinition GetResearchGroup(String subtype)
public DictionaryValuesReader<MyDefinitionId, MyResearchGroupDefinition> GetResearchGroupDefinitions()
public MyRespawnShipDefinition GetRespawnShipDefinition(String id)
public DictionaryReader<String, MyRespawnShipDefinition> GetRespawnShipDefinitions()
public ListReader<MyScenarioCategoryDefinition> GetScenarioCategoryDefinitions()
public MyScenarioDefinition GetScenarioDefinition(MyDefinitionId id)
public ListReader<MyScenarioDefinition> GetScenarioDefinitions()
public ListReader<MyPhysicalItemDefinition> GetSeedDefinitions()
public List<Tuple<MyObjectBuilder_Definitions, String>> GetSessionPreloadDefinitions()
public MyShipSoundsDefinition GetShipSoundsDefinition(MyDefinitionId id)
public ListReader<MySoundCategoryDefinition> GetSoundCategoryDefinitions()
public MyAudioDefinition GetSoundDefinition(MyStringHash subtypeId)
public DictionaryValuesReader<MyDefinitionId, MyAudioDefinition> GetSoundDefinitions()
public MySpawnGroupDefinition GetSpawnGroupDefinition(MyDefinitionId definition)
public MySpawnGroupDefinition GetSpawnGroupDefinition(Int32 index)
public ListReader<MySpawnGroupDefinition> GetSpawnGroupDefinitions()
public MyStoryCategoryDefinition GetStoryCategoryDefinition(MyDefinitionId id)
public MyStoryDefinition GetStoryDefinition(MyDefinitionId id)
public ListReader<MyStoryDefinition> GetStoryDefinitions(MyDefinitionId categoryDefinition)
public MyCubeBlockTagDefinition GetTagDefinition(String blockTypeKey)
public List<MyTargetingGroupDefinition> GetTargetingGroupDefinitions(List<MyTargetingGroupDefinition> list = null)
public ListReader<MyTransparentMaterialDefinition> GetTransparentMaterialDefinitions()
public ListReader<MyVoxelHandDefinition> GetVoxelHandDefinitions()
public ListReader<MyVoxelMapStorageDefinition> GetVoxelMapStorageDefinitions()
public ListReader<MyVoxelMapStorageDefinition> GetVoxelMapStorageDefinitionsForProceduralAdditions()
public ListReader<MyVoxelMapStorageDefinition> GetVoxelMapStorageDefinitionsForProceduralPrimaryAdditions()
public ListReader<MyVoxelMapStorageDefinition> GetVoxelMapStorageDefinitionsForProceduralRemovals()
public MyVoxelMaterialDefinition GetVoxelMaterialDefinition(String name)
public MyVoxelMaterialDefinition GetVoxelMaterialDefinition(Byte materialIndex)
public DictionaryValuesReader<String, MyVoxelMaterialDefinition> GetVoxelMaterialDefinitions()
public MyWeaponDefinition GetWeaponDefinition(MyDefinitionId id)
public ListReader<MyPhysicalItemDefinition> GetWeaponDefinitions()
public ListReader<MyWeatherEffectDefinition> GetWeatherDefinitions()
public MyWeatherEffectDefinition GetWeatherEffect(String subtype)
public DictionaryReader<String, MyWheelModelsDefinition> GetWheelModelDefinitions()
public Boolean HandItemExistsFor(MyDefinitionId physicalItemId)
public Boolean HasBlueprint(MyDefinitionId blueprintId)
public Boolean HasDefaultTargetingGroup(MyObjectBuilderType type, MyStringHash hash)
public Boolean HasRespawnShip(String id)
public Void InitVoxelMaterials()
public Boolean IsComponentBlock(MyDefinitionId blockDefinitionId)
public Nullable<MyDefinitionId> ItemIdFromWeaponId(MyDefinitionId weaponDefinition)
public Void LoadData(List<ModItem> mods)
public Void LoadScenarios()
public Void PreloadDefinitions()
public Void RegisterFactionDefinition(MyFactionDefinition definition)
public Void ReloadDecalMaterials()
public Void ReloadHandItems()
public Void ReloadParticles()
public Void ReloadPrefabsFromFile(String filePath)
public Void ReloadVoxelMaterials()
public Void Save(String filePattern = *.*)
public Void SaveHandItems()
public Void SetEntityContainerDefinition(MyContainerDefinition newDefinition)
public Void SetShipSoundSystem()
public Boolean TryGetAmmoDefinition(MyDefinitionId id, out ref MyAmmoDefinition ammoDefinition)
public MyAnimationDefinition TryGetAnimationDefinition(String animationSubtypeName)
public Boolean TryGetBlueprintDefinitionByResultId(MyDefinitionId resultId, out ref MyBlueprintDefinitionBase definition)
public MyBlueprintDefinitionBase TryGetBlueprintDefinitionByResultId(MyDefinitionId resultId)
public Boolean TryGetBotDefinition(MyDefinitionId id, out ref MyBotDefinition botDefinition)
public MyCubeBlockDefinition TryGetComponentBlockDefinition(MyDefinitionId componentDefId)
public Boolean TryGetComponentBlueprintDefinition(MyDefinitionId componentId, out ref MyBlueprintDefinitionBase componentBlueprint)
public virtual Boolean TryGetComponentDefinition(MyObjectBuilderType type, MyStringHash subtypeName, out ref MyComponentDefinitionBase componentDefinition)
public Boolean TryGetComponentDefinition(MyDefinitionId id, out ref MyComponentDefinition definition)
public virtual Boolean TryGetContainerDefinition(MyObjectBuilderType type, MyStringHash subtypeName, out ref MyContainerDefinition definition)
public Boolean TryGetContainerDefinition(MyDefinitionId containerId, out ref MyContainerDefinition definition)
public Boolean TryGetContractTypeDefinition(MyDefinitionId id, out ref MyContractTypeDefinition definition)
public Boolean TryGetCubeBlockDefinition(MyDefinitionId defId, out ref MyCubeBlockDefinition blockDefinition)
public Boolean TryGetDefinition<T>(MyDefinitionId defId, out ref T definition)
public MyCubeBlockDefinitionGroup TryGetDefinitionGroup(String groupName)
public Void TryGetDefinitionsByTypeId(MyObjectBuilderType typeId, HashSet<MyDefinitionId> definitions)
public Boolean TryGetEntityComponentDefinition(MyDefinitionId componentId, out ref MyComponentDefinitionBase definition)
public MyFactionDefinition TryGetFactionDefinition(String tag)
public Boolean TryGetGasPropertyDefinition(MyDefinitionId id, out ref MyGasProperties definition)
public MyHandItemDefinition TryGetHandItemDefinition(ref ref MyDefinitionId id)
public MyHandItemDefinition TryGetHandItemForPhysicalItem(MyDefinitionId physicalItemId)
public Boolean TryGetIngotBlueprintDefinition(MyDefinitionId oreId, out ref MyBlueprintDefinitionBase ingotBlueprint)
public Boolean TryGetIngotBlueprintDefinition(MyObjectBuilder_Base oreBuilder, out ref MyBlueprintDefinitionBase ingotBlueprint)
public Boolean TryGetPhysicalItemDefinition(MyDefinitionId id, out ref MyPhysicalItemDefinition definition)
public MyPhysicalItemDefinition TryGetPhysicalItemDefinition(MyDefinitionId id)
public Boolean TryGetSpawnGroupDefinition(String spawnGroupSubtypeName, out ref MySpawnGroupDefinition spawnGroupDefinition)
public Boolean TryGetVoxelMapStorageDefinition(String name, out ref MyVoxelMapStorageDefinition definition)
public Boolean TryGetVoxelMaterialDefinition(String name, out ref MyVoxelMaterialDefinition definition)
public Boolean TryGetWeaponDefinition(MyDefinitionId defId, out ref MyWeaponDefinition definition)
public Void UnloadData(Boolean clearPreloaded = False)
```

## `Sandbox.Game.Gui.MyTerminalAction`1`

- assembly: `Sandbox.Game`
- kind: class 
- base: `System.Object`
- interfaces: `Sandbox.Game.Gui.ITerminalAction`, `Sandbox.ModAPI.Interfaces.ITerminalAction`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalAction`

### Constructors
```csharp
public MyTerminalAction`1(String id, StringBuilder name, String icon)
public MyTerminalAction`1(String id, StringBuilder name, Action<TBlock> action, String icon)
public MyTerminalAction`1(String id, StringBuilder name, Action<TBlock, ListReader<TerminalActionParameter>> action, String icon)
public MyTerminalAction`1(String id, StringBuilder name, Action<TBlock> action, WriterDelegate<TBlock> valueWriter, String icon)
public MyTerminalAction`1(String id, StringBuilder name, Action<TBlock, ListReader<TerminalActionParameter>> action, WriterDelegate<TBlock> valueWriter, String icon)
public MyTerminalAction`1(String id, StringBuilder name, Action<TBlock> action, WriterDelegate<TBlock> valueWriter, String icon, Func<TBlock, Boolean> enabled = null, Func<TBlock, Boolean> callable = null)
```

### Fields
```csharp
public Func<TBlock, Boolean> Enabled
public Func<TBlock, Boolean> Callable
public List<MyToolbarType> InvalidToolbarTypes
public Boolean ValidForGroups
public WriterDelegate<TBlock> Writer
public Action<IList<TerminalActionParameter>, Action<Boolean>> DoUserParameterRequest
public Action<IUserCustomizableTerminalAction, Action<Boolean>> DoUserParameterRequestWithItem
```

### Properties
```csharp
public Boolean IsComponentBased { get; set; }
public Action<TBlock> Action { get; set; }
public Action<TBlock, ListReader<TerminalActionParameter>> ActionWithParameters { get; set; }
public String Id { get; }
public String Icon { get; }
public StringBuilder Name { get; }
public List<TerminalActionParameter> ParameterDefinitions { get; }
```

### Methods
```csharp
public Void Apply(MyTerminalBlock block, ListReader<TerminalActionParameter> parameters)
public Void Apply(MyTerminalBlock block)
public Boolean IsCallable(MyTerminalBlock block)
public Boolean IsEnabled(MyTerminalBlock block)
public Boolean IsValidForGroups()
public Boolean IsValidForToolbarType(MyToolbarType type)
public Void RequestParameterCollection(IList<TerminalActionParameter> parameters, Action<Boolean> callback)
public Void RequestParameterCollection(IUserCustomizableTerminalAction blockItem, Action<Boolean> callback)
public Void WriteValue(MyTerminalBlock block, StringBuilder appendTo)
```

## `Sandbox.Game.Gui.MyTerminalControl`1`

- assembly: `Sandbox.Game`
- kind: class abstract
- base: `System.Object`
- interfaces: `Sandbox.Game.Gui.ITerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`

### Constructors
```csharp
public MyTerminalControl`1(String id)
```

### Fields
```csharp
public readonly String Id
public Func<TBlock, Boolean> Enabled
public Func<TBlock, Boolean> Visible
public static readonly Single PREFERRED_CONTROL_WIDTH
public static readonly MyTerminalBlock[] Empty
```

### Properties
```csharp
public TooltipGetter<TBlock> DynamicTooltipGetter { get; set; }
public Boolean SupportsMultipleBlocks { get; set; }
public MyTerminalAction<TBlock>[] Actions { get; set; }
```

### Methods
```csharp
public MyGuiControlBase GetGuiControl()
public Void RedrawControl()
public Void UpdateVisual()
```

## `Sandbox.Game.Gui.MyTerminalControlButton`1`

- assembly: `Sandbox.Game`
- kind: class 
- base: ``
- interfaces: `Sandbox.Game.Gui.ITerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlButton`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Constructors
```csharp
public MyTerminalControlButton`1(String id, MyStringId title, MyStringId tooltip, Action<TBlock> action, Boolean isAutoscaleEnabled = False, Boolean isEllipsisEnabled = False)
```

### Fields
```csharp
public MyStringId Title
public MyStringId Tooltip
```

### Properties
```csharp
public TitleGetter<TBlock> DynamicTitleGetter { get; set; }
public Action<TBlock> Action { get; set; }
```

### Methods
```csharp
public MyTerminalAction<TBlock> EnableAction(String icon, StringBuilder name, WriterDelegate<TBlock> writer = null)
```

## `Sandbox.Game.Gui.MyTerminalControlFactory`

- assembly: `Sandbox.Game`
- kind: class abstract sealed
- base: `System.Object`

### Methods
```csharp
public static Void AddAction(Type blockType, ITerminalAction action)
public static Void AddAction<TComponent>(ITerminalAction action)
public static Void AddAction<TBase, TBlock>(MyTerminalAction<TBase> Action)
public static Void AddAction<TBlock>(Int32 index, MyTerminalAction<TBlock> Action)
public static Void AddAction<TBlock>(MyTerminalAction<TBlock> Action)
public static Void AddActions<TBlock>(MyTerminalControl<TBlock> block)
public static Void AddActions(Type blockType, ITerminalControl control)
public static Void AddBaseClass<TBlock, TBase>()
public static Void AddControl<TBlock>(Int32 index, MyTerminalControl<TBlock> control)
public static Void AddControl<TBlock>(MyTerminalControl<TBlock> control)
public static Void AddControl<TBase, TBlock>(MyTerminalControl<TBase> control)
public static Void AddControl(Type blockType, ITerminalControl control)
public static Void AddControl<TComponent>(ITerminalControl control)
public static Boolean AreControlsCreated<TBlock>()
public static Boolean AreControlsCreated(Type blockType)
public static Void EnsureControlsAreCreated(Type blockType)
public static Void GetActions<TBlock>(List<MyTerminalAction<TBlock>> resultList)
public static UniqueListReader<ITerminalAction> GetActions(Type blockType)
public static Void GetActions(Type blockType, List<ITerminalAction> resultList)
public static UniqueListReader<ITerminalControl> GetControls(Type blockType)
public static Void GetControls(Type blockType, List<ITerminalControl> resultList)
public static Void GetControls<TBlock>(List<MyTerminalControl<TBlock>> resultList)
public static Void GetValueControls(Type blockType, List<ITerminalProperty> resultList)
public static Void GetValueControls<TBlock>(Type blockType, List<ITerminalProperty> resultList)
public static Void RemoveAllBaseClass<TBlock>()
public static Void RemoveBaseClass<TBlock, TBase>()
public static Void RemoveControl<TBlock>(IMyTerminalControl item)
public static Void RemoveControl(Type blockType, IMyTerminalControl controlItem)
public static Void Unload()
```

## `Sandbox.Graphics.GUI.MyGuiControlBase`

- assembly: `Sandbox.Graphics`
- kind: class abstract
- base: `System.Object`
- interfaces: `Sandbox.Graphics.GUI.IMyGuiControlsOwner`, `VRage.IVRageGuiControl`, `Sandbox.Graphics.GUI.IMyTestControlDataProvider`

### Fields
```csharp
public Action<MyGuiControlBase> EnabledChanged
public static Boolean DEBUG_CONTROL_BORDERS
```

### Properties
```csharp
public List<MyGuiControlBase> VisibleElements { get; }
public Single Alpha { get; set; }
public Boolean CanAutoFocusOnInputHandling { get; set; }
public Boolean BlockAutofocusOnHandlingOnce { get; set; }
public Boolean CanFocusChildren { get; set; }
public String Name { get; set; }
public IMyGuiControlsOwner Owner { get; set; }
public MyGuiControls Elements { get; set; }
public MyToolTips Tooltips { get; }
public Vector2 Position { get; set; }
public String CustomId { get; set; }
public Thickness Margin { get; set; }
public Single PositionY { get; set; }
public Single PositionX { get; set; }
public Vector2 Size { get; set; }
public RectangleF Rectangle { get; }
public RectangleF FocusRectangle { get; }
public MyStringId GamepadHelpTextId { get; set; }
public String GamepadHelpText { get; set; }
public Vector2 MinSize { get; set; }
public Vector2 MaxSize { get; set; }
public Vector4 ColorMask { get; set; }
public MyGuiCompositeTexture BackgroundTexture { get; set; }
public Vector4 BorderColor { get; set; }
public Boolean ForceHighlight { get; set; }
public Boolean ForceUseOfUiOpacityForBackground { get; set; }
public Int32 BorderSize { get; set; }
public Boolean BorderEnabled { get; set; }
public Boolean BorderHighlightEnabled { get; set; }
public Boolean DrawWhilePaused { get; set; }
public Boolean SkipForMouseTest { get; set; }
public Boolean Enabled { get; set; }
public Boolean ShowTooltipWhenDisabled { get; set; }
public Boolean IsHitTestVisible { get; set; }
public Boolean IsActiveControl { get; set; }
public MyGuiDrawAlignEnum OriginAlign { get; set; }
public Boolean Visible { get; set; }
public MyGuiControlHighlightType HighlightType { get; set; }
public Boolean HasHighlight { get; set; }
public Boolean HasFocus { get; }
public Boolean IsMouseOver { get; set; }
public Boolean CanHaveFocus { get; set; }
public Boolean CanPlaySoundOnMouseOver { get; set; }
public Object UserData { get; set; }
public String DebugNamePath { get; }
public Int32 TooltipDelay { get; set; }
public Vector2 BorderMargin { get; set; }
public Boolean IsWithinScissor { get; set; }
public Boolean IsWithinDrawScissor { get; set; }
public String ToolTip { get; }
public String ControlType { get; }
public IMyNewContentNotificator NewContentNotificator { get; set; }
```

### Events
```csharp
public event EventHandler GamepadHelpTextChanged
public event VisibleChangedDelegate VisibleChanged
public event Action<MyGuiControlBase> HighlightChanged
public event Action<MyGuiControlBase> SizeChanged
public event Action<MyGuiControlBase, NameChangedArgs> NameChanged
public event Action<MyGuiControlBase, Boolean> MouseOverChanged
public event Action<MyGuiControlBase, Boolean> FocusChanged
```

### Methods
```csharp
public static Color ApplyColorMaskModifiers(Vector4 sourceColorMask, Boolean enabled, Single transitionAlpha)
public virtual Void CheckIsWithinScissor(RectangleF scissor, Boolean complete = False)
public static Boolean CheckMouseOver(Vector2 size, Vector2 position, MyGuiDrawAlignEnum originAlign)
public virtual Boolean CheckMouseOver(Boolean use_IsMouseOverAll = True)
public virtual Void Clear()
public virtual Void Draw(Single transitionAlpha, Single backgroundTransitionAlpha)
public virtual MyGuiControlGridDragAndDrop GetDragAndDropHandlingNow()
public Void GetElementsUnderCursor(Vector2 position, Boolean visibleOnly, List<MyGuiControlBase> controls)
public virtual MyGuiControlBase GetExclusiveInputHandler()
public static MyGuiControlBase GetExclusiveInputHandler(MyGuiControls controls)
public MyGuiControlBase GetFocusControl(MyDirection direction, Boolean page, Boolean loop)
public virtual String GetMouseCursorTexture()
public virtual MyGuiControlBase GetMouseOverControl()
public virtual MyGuiControlBase GetNextFocusControl(MyGuiControlBase currentFocusControl, MyDirection direction, Boolean page)
public virtual MyObjectBuilder_GuiControlBase GetObjectBuilder()
public Vector2 GetPositionAbsolute()
public Vector2 GetPositionAbsoluteBottomLeft()
public Vector2 GetPositionAbsoluteBottomRight()
public Vector2 GetPositionAbsoluteCenter()
public Vector2 GetPositionAbsoluteCenterLeft()
public Vector2 GetPositionAbsoluteCenterRight()
public Vector2 GetPositionAbsoluteTopLeft()
public Vector2 GetPositionAbsoluteTopRight()
public virtual Void GetScissorBounds(ref ref Vector2 topLeft, ref ref Vector2 botRight)
public virtual Nullable<RectangleF> GetScissoringArea()
public Nullable<Vector2> GetSize()
public virtual MyGuiControlBase HandleInput()
public virtual Void HideToolTip()
public virtual Void Init(MyObjectBuilder_GuiControlBase builder)
public virtual Boolean IsMouseOverAnyControl()
public static Boolean IsPointInside(Vector2 queryPoint, Vector2 size, Vector2 position, MyGuiDrawAlignEnum originAlign)
public virtual Void OnFocusChanged(Boolean focus)
public virtual Void OnFocusChanged(MyGuiControlBase control, Boolean focus)
public virtual Void OnRemoving()
public static Void ReadIfHasValue<T>(ref ref T target, Nullable<T> source)
public static Void ReadIfHasValue(ref ref Color target, Nullable<Vector4> source)
public Void SetMaxSize(Vector2 maxSize)
public Void SetMaxWidth(Single maxWidth)
public Void SetToolTip(String text)
public Void SetToolTip(MyToolTips toolTip)
public Void SetToolTip(MyStringId text)
public virtual Void ShowToolTip()
public virtual String ToString()
public virtual Void Update()
public virtual Void UpdateArrange()
public virtual Void UpdateMeasure()
```

## `Sandbox.Graphics.GUI.MyGuiControlButton`

- assembly: `Sandbox.Graphics`
- kind: class 
- base: `Sandbox.Graphics.GUI.MyGuiControlAutoScaleText`
- interfaces: `Sandbox.Graphics.GUI.IMyGuiControlsOwner`, `VRage.IVRageGuiControl`, `Sandbox.Graphics.GUI.IMyTestControlDataProvider`, `Sandbox.Graphics.GUI.IMyTestCheckedStateDataProvider`

### Constructors
```csharp
public MyGuiControlButton()
public MyGuiControlButton(Nullable<Vector2> position = null, MyGuiControlButtonStyleEnum visualStyle = Default, Nullable<Vector2> size = null, Nullable<Vector4> colorMask = null, MyGuiDrawAlignEnum originAlign = HORISONTAL_CENTER_AND_VERTICAL_CENTER, String toolTip = null, StringBuilder text = null, Single textScale = 0.8, MyGuiDrawAlignEnum textAlignment = HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType highlightType = WHEN_CURSOR_OVER, Action<MyGuiControlButton> onButtonClick = null, GuiSounds cueEnum = MouseClick, Single buttonScale = 1, Nullable<Int32> buttonIndex = null, Boolean activateOnMouseRelease = False, Boolean isAutoscaleEnabled = False, Boolean isEllipsisEnabled = False, Action<MyGuiControlButton> onSecondaryButtonClick = null)
```

### Fields
```csharp
public Single IconScale
public Boolean ClickCallbackRespectsEnabledState
public Boolean ClickSoundRespectsVisibleState
public Boolean Selected
public MyGuiDrawAlignEnum TextAlignment
public String TextFont
public Boolean DrawCrossTextureWhenDisabled
public Nullable<MyGuiHighlightTexture> Icon
public MyGuiDrawAlignEnum IconOriginAlign
public static Boolean DEBUG_SHOW_NAME
```

### Properties
```csharp
public Single IconRotation { get; set; }
public Boolean Checked { get; set; }
public Boolean ActivateOnMouseRelease { get; set; }
public Int32 Index { get; set; }
public String Text { get; set; }
public MyStringId TextEnum { get; set; }
public GuiSounds CueEnum { get; set; }
public Single TextScale { get; set; }
public Single TextScaleWithLanguage { get; set; }
public MyGuiControlButtonStyleEnum VisualStyle { get; set; }
public StyleDefinition CustomStyle { get; set; }
```

### Events
```csharp
public event Action<MyGuiControlButton> ButtonClicked
public event Action<MyGuiControlButton> SecondaryButtonClicked
```

### Methods
```csharp
public Void ClearClickAction()
public virtual Void Draw(Single transitionAlpha, Single backgroundTransitionAlpha)
public virtual String GetMouseCursorTexture()
public virtual MyObjectBuilder_GuiControlBase GetObjectBuilder()
public static StyleDefinition GetVisualStyle(MyGuiControlButtonStyleEnum style)
public virtual MyGuiControlBase HandleInput()
public virtual Void Init(MyObjectBuilder_GuiControlBase objectBuilder)
public virtual Void OnFocusChanged(Boolean focus)
public Void PressButton(Boolean isPrimary = True)
public Void RaiseButtonClicked()
public Void SetMinSize(Vector2 minSize)
```

## `Sandbox.Graphics.GUI.MyGuiControlLabel`

- assembly: `Sandbox.Graphics`
- kind: class 
- base: `Sandbox.Graphics.GUI.MyGuiControlAutoScaleText`
- interfaces: `Sandbox.Graphics.GUI.IMyGuiControlsOwner`, `VRage.IVRageGuiControl`, `Sandbox.Graphics.GUI.IMyTestControlDataProvider`, `Sandbox.Graphics.GUI.IMyTestTextDataProvider`

### Constructors
```csharp
public MyGuiControlLabel()
public MyGuiControlLabel(Nullable<Vector2> position = null, Nullable<Vector2> size = null, String text = null, Nullable<Vector4> colorMask = null, Single textScale = 0.8, String font = Blue, MyGuiDrawAlignEnum originAlign = HORISONTAL_LEFT_AND_VERTICAL_CENTER, Boolean isAutoEllipsisEnabled = False, Single maxWidth = Infinity, Boolean isAutoScaleEnabled = False, Single minimumTextScale = 0.7)
```

### Fields
```csharp
public Boolean UseRichDraw
public StringBuilder m_textToDraw
```

### Properties
```csharp
public MyGuiDrawAlignEnum DrawAlign { get; set; }
public String Font { get; set; }
public String Text { get; set; }
public MyStringId TextEnum { get; set; }
public Single TextScale { get; set; }
public Single TextScaleWithLanguage { get; set; }
public StringBuilder TextToDraw { get; set; }
public Boolean UseTextShadow { get; set; }
```

### Methods
```csharp
public Void ApplyStyle(StyleDefinition style)
public Void Autowrap(Single width)
public Void DoEllipsisAndScaleAdjust(Boolean RecalculateSize = False, Single maxScale = Infinity, Boolean resetEllipsis = False)
public virtual Void Draw(Single transitionAlpha, Single backgroundTransitionAlpha)
public virtual MyObjectBuilder_GuiControlBase GetObjectBuilder()
public Vector2 GetTextSize()
public virtual Void Init(MyObjectBuilder_GuiControlBase objectBuilder)
public Void PrepareForAsyncTextUpdate()
public Void RecalculateSize()
public Void RefreshInternals()
public Void SetMinTextScale(Single minTextScale)
public Void UpdateFormatParams(Object[] args)
```

## `Sandbox.Graphics.GUI.MyGuiControls`

- assembly: `Sandbox.Graphics`
- kind: class 
- base: `Sandbox.Graphics.GUI.MyGuiControlBase+Friend`
- interfaces: `System.Collections.Generic.IEnumerable`1[[Sandbox.Graphics.GUI.MyGuiControlBase, Sandbox.Graphics, Version=0.1.1.0, Culture=neutral, PublicKeyToken=null]]`, `System.Collections.IEnumerable`

### Constructors
```csharp
public MyGuiControls(IMyGuiControlsOwner owner)
```

### Properties
```csharp
public MyGuiControlBase Item { get; set; }
public Int32 Count { get; }
```

### Events
```csharp
public event Action<MyGuiControls> CollectionChanged
public event Action<MyGuiControls> CollectionMembersVisibleChanged
```

### Methods
```csharp
public Void Add(MyGuiControlBase control, Int32 preferredControlIndex = -1)
public Void AddWeak(MyGuiControlBase control)
public Void Clear()
public Void ClearWeaks()
public Boolean Contains(MyGuiControlBase control)
public Int32 FindIndex(Predicate<MyGuiControlBase> match)
public MyGuiControlBase GetControlByName(String name)
public Enumerator<MyGuiControlBase> GetEnumerator()
public MyObjectBuilder_GuiControls GetObjectBuilder()
public List<MyGuiControlBase> GetVisibleControls()
public Int32 IndexOf(MyGuiControlBase item)
public Void Init(MyObjectBuilder_GuiControls objectBuilder)
public Boolean Remove(MyGuiControlBase control)
public Boolean RemoveControlByName(String name)
```

## `Sandbox.Graphics.GUI.MyGuiControlScrollablePanel`

- assembly: `Sandbox.Graphics`
- kind: class 
- base: `Sandbox.Graphics.GUI.MyGuiControlBase`
- interfaces: `Sandbox.Graphics.GUI.IMyGuiControlsOwner`, `VRage.IVRageGuiControl`, `Sandbox.Graphics.GUI.IMyTestControlDataProvider`, `Sandbox.Graphics.GUI.IMyGuiControlsParent`

### Constructors
```csharp
public MyGuiControlScrollablePanel(MyGuiControlBase scrolledControl)
```

### Fields
```csharp
public Vector2 ScrollBarOffset
public Single ScrollBarHScale
public Single ScrollBarVScale
public Vector2 ContentOffset
public Boolean DrawScrollBarSeparator
public Boolean CompleteScissor
```

### Properties
```csharp
public Single ScrollbarHSizeX { get; }
public Single ScrollbarHSizeY { get; }
public Single ScrollbarVSizeX { get; }
public Single ScrollbarVSizeY { get; }
public Boolean ScrollbarHWheel { get; set; }
public Boolean ScrollbarHEnabled { get; set; }
public Boolean ScrollbarVEnabled { get; set; }
public MyGuiControlBase ScrolledControl { get; set; }
public Vector2 ScrolledAreaSize { get; }
public MyGuiBorderThickness ScrolledAreaPadding { get; set; }
public Single ScrollbarVPosition { get; set; }
public MyGuiControls Controls { get; }
```

### Events
```csharp
public event Action<MyGuiControlScrollablePanel> PanelScrolled
```

### Methods
```csharp
public Boolean CheckChildFocus()
public virtual Void Draw(Single transitionAlpha, Single backgroundTransitionAlpha)
public Void FitSizeToScrolledControl()
public virtual MyGuiControlBase GetNextFocusControl(MyGuiControlBase currentFocusControl, MyDirection direction, Boolean page)
public virtual Nullable<RectangleF> GetScissoringArea()
public virtual MyGuiControlBase HandleInput()
public virtual Void OnFocusChanged(MyGuiControlBase control, Boolean focus)
public Void RefreshInternals()
public Void SetPageVertical(Single pageNumber)
public Void SetVerticalScrollbarValue(Single value)
public virtual Void ShowToolTip()
```

## `Sandbox.Graphics.GUI.MyGuiScreenBase`

- assembly: `Sandbox.Graphics`
- kind: class abstract
- base: `System.Object`
- interfaces: `Sandbox.Graphics.GUI.IMyGuiControlsParent`, `Sandbox.Graphics.GUI.IMyGuiControlsOwner`, `VRage.IVRageGuiScreen`, `Sandbox.Graphics.GUI.IMyTestScreenInputBindings`

### Fields
```csharp
public readonly MyGuiControls Elements
public Boolean IsHitTestVisible
public Action OnLoadingAction
public static readonly String GAMEPAD_HELP_LABEL_NAME
public static Boolean EnableSlowTransitionAnimations
```

### Properties
```csharp
public Boolean FirstUpdateServed { get; set; }
public Color BackgroundFadeColor { get; }
public MyGuiDrawAlignEnum Align { get; set; }
public Nullable<MyStringId> GamepadHelpTextId { get; set; }
public String GamepadHelpText { get; set; }
public Boolean SkipTransition { get; set; }
public Boolean Cancelled { get; set; }
public Boolean JoystickAsMouse { get; set; }
public MyGuiScreenState State { get; set; }
public Boolean IsLoaded { get; set; }
public Boolean EnabledBackgroundFade { get; set; }
public Boolean CanBeHidden { get; set; }
public Boolean CanHideOthers { get; set; }
public Boolean CanHaveFocus { get; set; }
public MyGuiControls Controls { get; }
public Nullable<Vector4> BackgroundColor { get; set; }
public Nullable<Vector2> Size { get; set; }
public List<MyGuiControlBase> VisibleElements { get; }
public Boolean Visible { get; }
public Vector2 CloseButtonOffset { get; set; }
public Boolean CloseButtonEnabled { get; set; }
public MyGuiControlButtonStyleEnum CloseButtonStyle { get; set; }
public MyGuiControlBase FocusedControl { get; set; }
public String DebugNamePath { get; }
public String Name { get; }
public IMyGuiControlsOwner Owner { get; }
public String[] InputBindings { get; }
public Boolean IsOpened { get; }
```

### Events
```csharp
public event Action<String, Vector2, UInt32> MouseClickEvent
public event ScreenHandler Closed
public event VisibleChangedDelegate VisibleChanged
public event Action<MyGuiScreenBase> DataLoading
public event Action<MyGuiScreenBase> DataUnloading
public event Action OnFocusChangedToControlOutOfDrawScissors
public event Action<MyGuiControlBase, MyGuiControlBase> FocusChanged
```

### Methods
```csharp
public Void AddControl(IVRageGuiControl control)
public Boolean CanCloseInCloseAllScreenCalls()
public Boolean CanShareInput()
public virtual Boolean CloseScreen(Boolean isUnloading = False)
public virtual Void CloseScreenNow(Boolean isUnloading = False)
public Boolean ContainsControl(IVRageGuiControl control)
public virtual Boolean Draw()
public Void ForceFocus(MyGuiControlBase focus, Boolean forceHighlight = False)
public MyGuiControlBase GetControlByName(String controlName)
public virtual Void GetControlsUnderMouseCursor(Vector2 position, List<MyGuiControlBase> controls, Boolean visibleOnly)
public Boolean GetDrawMouseCursor()
public Boolean GetDrawScreenEvenWithoutFocus()
public abstract String GetFriendlyName()
public MyGuiControlBase GetMouseOverControl()
public MyGuiControlBase GetNextFocusControl(MyGuiControlBase currentFocusControl, MyDirection direction, Boolean page)
public MyGuiControlBase GetNextFocusInDirection(MyDirection direction)
public MyObjectBuilder_GuiScreen GetObjectBuilder()
public Vector2 GetPosition()
public Vector2 GetPositionAbsolute()
public Vector2 GetPositionAbsoluteCenter()
public Vector2 GetPositionAbsoluteTopLeft()
public virtual Nullable<RectangleF> GetScissoringArea()
public Nullable<Vector2> GetSize()
public virtual Int32 GetTransitionClosingTime()
public virtual Int32 GetTransitionOpeningTime()
public virtual Void HandleInput(Boolean receivedFocusInThisUpdate)
public virtual Boolean HandleInputAfterSimulation()
public virtual Void HandleUnhandledInput(Boolean receivedFocusInThisUpdate)
public virtual Boolean HideScreen()
public Void HideTooltips()
public Void Init(MyObjectBuilder_GuiScreen objectBuilder)
public virtual Void InputLost()
public Boolean IsAlwaysFirst()
public Boolean IsFirstForUnload()
public Boolean IsMouseOverAnyControl()
public Boolean IsTopMostScreen()
public Boolean IsTopScreen()
public virtual Void LoadContent()
public virtual Void LoadData()
public Void OnFocusChanged(MyGuiControlBase control, Boolean focus)
public virtual Void OnRemoved()
public virtual Void OnScreenOrderChanged(MyGuiScreenBase oldLast, MyGuiScreenBase newLast)
public virtual Void PrepareDraw()
public virtual Void RecreateControls(Boolean constructor)
public virtual Boolean RegisterClicks()
public Boolean RemoveControl(IVRageGuiControl control)
public virtual Void RunLoadingAction()
public virtual Boolean UnhideScreen()
public virtual Void UnloadContent()
public virtual Void UnloadData()
public virtual Boolean Update(Boolean hasFocus)
public Void UpdateGamepadHelp(MyGuiControlBase control)
```

## `Sandbox.Graphics.GUI.MyScreenManager`

- assembly: `Sandbox.Graphics`
- kind: class abstract sealed
- base: `System.Object`

### Fields
```csharp
public static Int32 TotalGamePlayTimeInMilliseconds
public static Func<StringBuilder, String> OnValidateText
public static Func<String, String> FilterOffensiveText
```

### Properties
```csharp
public static MyGuiScreenBase LastScreenWithFocus { get; }
public static Thread UpdateThread { get; set; }
public static MyGuiControlBase FocusedControl { get; }
public static Boolean InputToNonFocusedScreens { get; set; }
public static IEnumerable<MyGuiScreenBase> Screens { get; }
```

### Events
```csharp
public event Action<MyGuiScreenBase> ScreenAdded
public event Action<MyGuiScreenBase> ScreenRemoved
public event Action EndOfDraw
public event Action<MyGuiScreenBase> BeforeScreenDraw
```

### Methods
```csharp
public static Void AddScreen(MyGuiScreenBase screen)
public static Void AddScreenNow(MyGuiScreenBase screen)
public static Void CheckThread()
public static Void ClearLastScreenWithFocus()
public static Void CloseAllScreensExcept(MyGuiScreenBase dontRemove)
public static Void CloseAllScreensExceptThisOneAndAllTopMost(MyGuiScreenBase dontRemove)
public static Void CloseAllScreensNowExcept(MyGuiScreenBase dontRemove, Boolean isUnloading = False)
public static Void CloseNowAllBelow(MyGuiScreenBase targetScreen)
public static Void CloseScreen(Type screenType)
public static Void CloseScreenNow(Type screenType)
public static Void Draw()
public static Boolean ExistsScreenOfType(Type screenType)
public static String FilterOffensiveTextInput(String text)
public static Void GetControlsUnderMouseCursor(List<MyGuiControlBase> outControls, Boolean visibleOnly)
public static T GetFirstScreenOfType<T>()
public static StringBuilder GetGuiScreensForDebug()
public static MyGuiScreenBase GetPreviousScreen(MyGuiScreenBase screen, Predicate<MyGuiScreenBase> condition, Predicate<MyGuiScreenBase> terminatingCondition)
public static Int32 GetScreensCount()
public static MyGuiScreenBase GetScreenWithFocus()
public static MyGuiScreenBase GetTopHiddenScreen()
public static Void HandleInput()
public static Void HandleInputAfterSimulation()
public static Void InsertScreen(MyGuiScreenBase screen, Int32 index)
public static Boolean IsAnyScreenOpening()
public static Boolean IsScreenOfTypeOpen(String debugNamePath)
public static Boolean IsScreenOfTypeOpen(Type screenType)
public static Boolean IsScreenOnTop(MyGuiScreenBase screen)
public static Void LoadContent()
public static Void LoadData()
public static Void RecreateControls()
public static Void RemoveAllScreensExcept(MyGuiScreenBase dontRemove)
public static Void RemoveScreen(MyGuiScreenBase screen)
public static Void RemoveScreenByType(Type screenType)
public static Void UnloadContent()
public static Void Update(Int32 totalTimeInMS)
public static String ValidateText(StringBuilder text)
```

## `Sandbox.Graphics.MyGuiManager`

- assembly: `Sandbox.Graphics`
- kind: class abstract sealed
- base: `System.Object`

### Fields
```csharp
public static Int32 TotalTimeInMilliseconds
public static Int32 FAREST_TIME_IN_PAST = -60000
public static Single MIN_TEXT_SCALE = 0.7
```

### Properties
```csharp
public static MatrixD Camera { get; }
public static MatrixD CameraView { get; }
public static Boolean FullscreenHudEnabled { get; set; }
public static Boolean XboxSpecificTooltipPositioning { get; set; }
public static Single UIScale { get; set; }
public static MyLanguagesEnum CurrentLanguage { get; set; }
public static Vector2 MouseCursorPosition { get; set; }
```

### Methods
```csharp
public static Vector2 ComputeFullscreenGuiCoordinate(MyGuiDrawAlignEnum align, Int32 pixelOffsetX = 54, Int32 pixelOffsetY = 54)
public static Void DrawBorders(Vector2 topLeftPosition, Vector2 size, Color color, Int32 borderSize)
public static Void DrawRectangle(Vector2 topLeftPosition, Vector2 size, Color color)
public static Void DrawSprite(String texture, Vector2 position, Color color, Boolean ignoreBounds, Boolean waitTillLoaded)
public static Void DrawSprite(String texture, Vector2 position, Nullable<Rectangle> sourceRectangle, Color color, Single rotation, Vector2 scale, Boolean ignoreBounds, Boolean waitTillLoaded)
public static Void DrawSprite(String texture, Rectangle rectangle, Color color, Boolean ignoreBounds, Boolean waitTillLoaded, String maskTexture = null, Single rotation = 0, Single rotSpeed = 0)
public static Void DrawSpriteBatch(String texture, Vector2 normalizedCoord, Vector2 normalizedSize, Color color, MyGuiDrawAlignEnum drawAlign, Boolean useFullClientArea = False, Boolean waitTillLoaded = True, String maskTexture = null, Single rotation = 0, Single rotSpeed = 0, Boolean ignoreBounds = False)
public static Void DrawSpriteBatch(String texture, Vector2 normalizedCoord, Single normalizedWidth, Int32 screenHeight, Color color, MyGuiDrawAlignEnum drawAlign, Boolean waitTillLoaded = True)
public static Void DrawSpriteBatch(String texture, Vector2 normalizedCoord, Int32 screenWidth, Single normalizedHeight, Color color, MyGuiDrawAlignEnum drawAlign, Boolean waitTillLoaded = True)
public static Void DrawSpriteBatch(String texture, Vector2 pos, Color color, Boolean waitTillLoaded = True)
public static Void DrawSpriteBatch(String texture, Rectangle destinationRectangle, Color color, Boolean ignoreBounds, Boolean waitTillLoaded)
public static Void DrawSpriteBatch(String texture, Int32 x, Int32 y, Int32 width, Int32 height, Color color, Boolean waitTillLoaded = True)
public static Void DrawSpriteBatchRoundUp(String texture, Vector2 normalizedCoord, Vector2 normalizedSize, Color color, MyGuiDrawAlignEnum drawAlign, Boolean waitTillLoaded = True)
public static Void DrawString(MyStringHash font, String text, Vector2 normalizedCoord, Single scale, Nullable<Color> colorMask = null, MyGuiDrawAlignEnum drawAlign = HORISONTAL_LEFT_AND_VERTICAL_TOP, Boolean useFullClientArea = False, Single maxTextWidth = Infinity, Boolean ignoreBounds = False)
public static Void DrawString(String font, StringBuilder text, Vector2 normalizedCoord, Single scale, Nullable<Color> colorMask = null, MyGuiDrawAlignEnum drawAlign = HORISONTAL_LEFT_AND_VERTICAL_TOP, Boolean useFullClientArea = False, Single maxTextWidth = Infinity, Boolean ignoreBounds = False)
public static Void DrawString(String font, String text, Vector2 normalizedCoord, Single scale, Nullable<Color> colorMask = null, MyGuiDrawAlignEnum drawAlign = HORISONTAL_LEFT_AND_VERTICAL_TOP, Boolean useFullClientArea = False, Single maxTextWidth = Infinity, Boolean ignoreBounds = False)
public static String GetBackgroundTextureFilenameByAspectRatio(Vector2 normalizedSize)
public static MyFont GetFont(MyStringHash fontId)
public static Single GetFontHeight(String font, Single scale)
public static Single GetFullsceenAspectRatio()
public static Rectangle GetFullscreenRectangle()
public static Vector2 GetHudNormalizedCoordFromPixelCoord(Vector2 pixelCoord)
public static Vector2 GetHudNormalizedSizeFromPixelSize(Vector2 pixelSize)
public static Vector2 GetHudPixelCoordFromNormalizedCoord(Vector2 normalizedCoord)
public static Vector2 GetHudSize()
public static Vector2 GetHudSizeHalf()
public static Vector2 GetMaxMouseCoord()
public static Vector2 GetMinMouseCoord()
public static String GetMouseCursorTexture()
public static Vector2 GetNormalizedCoordinateFromScreenCoordinate(Vector2 screenCoord)
public static Vector2 GetNormalizedCoordinateFromScreenCoordinate_FULLSCREEN(Vector2 fullScreenCoord)
public static Vector2 GetNormalizedMousePosition(Vector2 mousePosition, Vector2 mouseAreaSize)
public static Vector2 GetNormalizedSize(Vector2 size, Single scale)
public static Vector2 GetNormalizedSizeFromScreenSize(Vector2 screenSize)
public static Single GetPixelsFromFloat(Single size)
public static Void GetSafeAspectRatioFullScreenPictureSize(Vector2I originalSize, out ref Rectangle outRect)
public static Rectangle GetSafeFullscreenRectangle()
public static Rectangle GetSafeGuiRectangle()
public static Void GetSafeHeightFullScreenPictureSize(Vector2I originalSize, out ref Rectangle outRect)
public static Single GetSafeScreenScale()
public static Vector2 GetScreenCoordinateFromNormalizedCoordinate(Vector2 normalizedCoord, Boolean useFullClientArea = False)
public static Vector2 GetScreenSizeFromNormalizedSize(Vector2 normalizedSize, Boolean useFullClientArea = False)
public static Vector2 GetScreenTextLeftBottomPosition()
public static Vector2 GetScreenTextLeftTopPosition()
public static Vector2 GetScreenTextRightBottomPosition()
public static Vector2 GetScreenTextRightTopPosition()
public static Void InitFont(MyFontDefinition fontDefinition)
public static Void InitFonts(IEnumerable<MyFontDefinition> fontDefinitions)
public static Void InitFonts()
public static Boolean IsDebugScreenEnabled()
public static Void LoadContent()
public static Void LoadData()
public static Single MeasureScaleNeeded(String font, String text, Single width)
public static Single MeasureScaleNeeded(String font, StringBuilder text, Single width)
public static Vector2 MeasureString(String font, StringBuilder text, Single scale)
public static Vector2 MeasureString(String font, String text, Single scale)
public static Vector2 MeasureString(MyStringHash font, String text, Single scale)
public static Vector2 MeasureStringRaw(String font, String text, Single scale)
public static Vector2 MeasureStringRaw(String font, StringBuilder text, Single scale)
public static Void SetCamera(MyCamera camera)
public static Void SetMouseCursorTexture(String texture)
public static List<String> SplitIntoSameHeightStrings(String font, String text)
public static List<String> SplitIntoSameHeightStrings(String font, StringBuilder text)
public static Void Update(Int32 totalTimeInMS)
public static Boolean UpdateScreenSize(Vector2I screenSize, Vector2I screenSizeHalf, Boolean isTriple)
public static SpriteScissorToken UsingScissorRectangle(ref ref RectangleF normalizedRectangle)
```

## `Sandbox.ModAPI.CustomActionGetDelegate`

- assembly: `Sandbox.Common`
- kind: class sealed
- base: `System.MulticastDelegate`
- interfaces: `System.ICloneable`, `System.Runtime.Serialization.ISerializable`

### Constructors
```csharp
public CustomActionGetDelegate(Object object, IntPtr method)
```

### Methods
```csharp
public virtual IAsyncResult BeginInvoke(IMyTerminalBlock block, List<IMyTerminalAction> actions, AsyncCallback callback, Object object)
public virtual Void EndInvoke(IAsyncResult result)
public virtual Void Invoke(IMyTerminalBlock block, List<IMyTerminalAction> actions)
```

## `Sandbox.ModAPI.CustomControlGetDelegate`

- assembly: `Sandbox.Common`
- kind: class sealed
- base: `System.MulticastDelegate`
- interfaces: `System.ICloneable`, `System.Runtime.Serialization.ISerializable`

### Constructors
```csharp
public CustomControlGetDelegate(Object object, IntPtr method)
```

### Methods
```csharp
public virtual IAsyncResult BeginInvoke(IMyTerminalBlock block, List<IMyTerminalControl> controls, AsyncCallback callback, Object object)
public virtual Void EndInvoke(IAsyncResult result)
public virtual Void Invoke(IMyTerminalBlock block, List<IMyTerminalControl> controls)
```

## `Sandbox.ModAPI.IMyEventControllerBlock`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Ingame.IMyEventControllerBlock`, `Sandbox.ModAPI.Ingame.IMyFunctionalBlock`, `Sandbox.ModAPI.Ingame.IMyTerminalBlock`, `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `Sandbox.ModAPI.IMyFunctionalBlock`, `Sandbox.ModAPI.IMyTerminalBlock`, `VRage.Game.ModAPI.IMyCubeBlock`, `VRage.ModAPI.IMyEntity`

### Properties
```csharp
public IMyEventControllerEntityComponent SelectedEvent { get; }
```

### Events
```csharp
public event Action<Int32> ActionTriggered
```

### Methods
```csharp
public abstract Void TriggerAction(Int32 startIndex)
```

## `Sandbox.ModAPI.IMySensorBlock`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.IMyFunctionalBlock`, `Sandbox.ModAPI.Ingame.IMyFunctionalBlock`, `Sandbox.ModAPI.Ingame.IMyTerminalBlock`, `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `Sandbox.ModAPI.IMyTerminalBlock`, `VRage.Game.ModAPI.IMyCubeBlock`, `VRage.ModAPI.IMyEntity`, `Sandbox.ModAPI.Ingame.IMySensorBlock`

### Properties
```csharp
public Vector3 FieldMin { get; set; }
public Vector3 FieldMax { get; set; }
```

### Events
```csharp
public event Action<Boolean> StateChanged
```

## `Sandbox.ModAPI.IMyTerminalActionsHelper`

- assembly: `Sandbox.Common`
- kind: interface 

### Methods
```csharp
public abstract Void GetActions(Type blockType, List<ITerminalAction> resultList, Func<ITerminalAction, Boolean> collect = null)
public abstract ITerminalAction GetActionWithName(String actionId, Type blockType)
public abstract Void GetProperties(Type blockType, List<ITerminalProperty> resultList, Func<ITerminalProperty, Boolean> collect = null)
public abstract ITerminalProperty GetProperty(String id, Type blockType)
public abstract IMyGridTerminalSystem GetTerminalSystemForGrid(IMyCubeGrid grid)
public abstract Void SearchActionsOfName(String name, Type blockType, List<ITerminalAction> resultList, Func<ITerminalAction, Boolean> collect = null)
```

## `Sandbox.ModAPI.IMyTerminalBlock`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `VRage.ModAPI.IMyEntity`, `Sandbox.ModAPI.Ingame.IMyTerminalBlock`

### Properties
```csharp
public Boolean IsDetailedInfoDirty { get; }
```

### Events
```csharp
public event Action<IMyTerminalBlock> CustomDataChanged
public event Action<IMyTerminalBlock> CustomNameChanged
public event Action<IMyTerminalBlock> OwnershipChanged
public event Action<IMyTerminalBlock> PropertiesChanged
public event Action<IMyTerminalBlock> ShowOnHUDChanged
public event Action<IMyTerminalBlock> VisibilityChanged
public event Action<IMyTerminalBlock, StringBuilder> AppendingCustomInfo
```

### Methods
```csharp
public abstract Void ClearDetailedInfo()
public abstract StringBuilder GetDetailedInfo()
public abstract Boolean IsInSameLogicalGroupAs(IMyTerminalBlock other)
public abstract Boolean IsSameConstructAs(IMyTerminalBlock other)
public abstract Void RefreshCustomInfo()
public abstract Void SetDetailedInfoDirty()
```

## `Sandbox.ModAPI.IMyTerminalControls`

- assembly: `Sandbox.Common`
- kind: interface 

### Events
```csharp
public event CustomControlGetDelegate CustomControlGetter
public event CustomActionGetDelegate CustomActionGetter
```

### Methods
```csharp
public abstract Void AddAction<TBlock>(IMyTerminalAction action)
public abstract Void AddControl<TBlock>(IMyTerminalControl item)
public abstract IMyTerminalAction CreateAction<TBlock>(String id)
public abstract TControl CreateControl<TControl, TBlock>(String id)
public abstract IMyTerminalControlProperty<TValue> CreateProperty<TValue, TBlock>(String id)
public abstract Void GetActions<TBlock>(out ref List<IMyTerminalAction> items)
public abstract Void GetControls<TBlock>(out ref List<IMyTerminalControl> items)
public abstract Void RemoveAction<TBlock>(IMyTerminalAction action)
public abstract Void RemoveControl<TBlock>(IMyTerminalControl item)
```

## `Sandbox.ModAPI.Ingame.IMyTerminalBlock`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`

### Properties
```csharp
public String CustomName { get; set; }
public String CustomNameWithFaction { get; }
public String DetailedInfo { get; }
public String CustomInfo { get; }
public String CustomData { get; set; }
public Boolean ShowOnHUD { get; set; }
public Boolean ShowInTerminal { get; set; }
public Boolean ShowInToolbarConfig { get; set; }
public Boolean ShowInInventory { get; set; }
```

### Methods
```csharp
public abstract Void GetActions(List<ITerminalAction> resultList, Func<ITerminalAction, Boolean> collect = null)
public abstract ITerminalAction GetActionWithName(String name)
public abstract Void GetProperties(List<ITerminalProperty> resultList, Func<ITerminalProperty, Boolean> collect = null)
public abstract ITerminalProperty GetProperty(String id)
public abstract Boolean HasLocalPlayerAccess()
public abstract Boolean HasNobodyPlayerAccessToBlock()
public abstract Boolean HasPlayerAccess(Int64 playerId, MyRelationsBetweenPlayerAndBlock defaultNoUser = NoOwnership)
public abstract Boolean HasPlayerAccessWithNobodyCheck(Int64 playerId, Boolean isForPB = False)
public abstract Boolean IsSameConstructAs(IMyTerminalBlock other)
public abstract Void SearchActionsOfName(String name, List<ITerminalAction> resultList, Func<ITerminalAction, Boolean> collect = null)
public abstract Void SetCustomName(String text)
public abstract Void SetCustomName(StringBuilder text)
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalAction`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.ITerminalAction`

### Properties
```csharp
public Func<IMyTerminalBlock, Boolean> Enabled { get; set; }
public List<MyToolbarType> InvalidToolbarTypes { get; set; }
public Boolean ValidForGroups { get; set; }
public StringBuilder Name { get; set; }
public String Icon { get; set; }
public Action<IMyTerminalBlock> Action { get; set; }
public Action<IMyTerminalBlock, StringBuilder> Writer { get; set; }
public List<TerminalActionParameter> ParameterDefinitions { get; }
public Action<IMyTerminalBlock, ListReader<TerminalActionParameter>> ActionWithParameters { get; set; }
public Action<IUserCustomizableTerminalAction, Action<Boolean>> DoUserParameterRequestWithItem { get; set; }
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`

- assembly: `Sandbox.Common`
- kind: interface 

### Properties
```csharp
public String Id { get; }
public Func<IMyTerminalBlock, Boolean> Enabled { get; set; }
public Func<IMyTerminalBlock, Boolean> Visible { get; set; }
public Boolean SupportsMultipleBlocks { get; set; }
```

### Methods
```csharp
public abstract Void RedrawControl()
public abstract Void UpdateVisual()
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlButton`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Properties
```csharp
public Action<IMyTerminalBlock> Action { get; set; }
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCheckbox`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1[[System.Boolean, System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]`, `Sandbox.ModAPI.Interfaces.ITerminalProperty`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Properties
```csharp
public MyStringId OnText { get; set; }
public MyStringId OffText { get; set; }
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCombobox`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1[[System.Int64, System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]`, `Sandbox.ModAPI.Interfaces.ITerminalProperty`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Properties
```csharp
public Action<List<MyTerminalControlComboBoxItem>> ComboBoxContent { get; set; }
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlOnOffSwitch`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1[[System.Boolean, System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]`, `Sandbox.ModAPI.Interfaces.ITerminalProperty`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Properties
```csharp
public MyStringId OnText { get; set; }
public MyStringId OffText { get; set; }
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlSlider`

- assembly: `Sandbox.Common`
- kind: interface 
- interfaces: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1[[System.Single, System.Private.CoreLib, Version=11.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]`, `Sandbox.ModAPI.Interfaces.ITerminalProperty`, `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

### Properties
```csharp
public Action<IMyTerminalBlock, StringBuilder> Writer { get; set; }
```

### Methods
```csharp
public abstract Void SetDualLogLimits(Single absMin, Single absMax, Single centerBand)
public abstract Void SetDualLogLimits(Func<IMyTerminalBlock, Single> minGetter, Func<IMyTerminalBlock, Single> maxGetter, Single centerBand)
public abstract Void SetLimits(Single min, Single max)
public abstract Void SetLimits(Func<IMyTerminalBlock, Single> minGetter, Func<IMyTerminalBlock, Single> maxGetter)
public abstract Void SetLogLimits(Single min, Single max)
public abstract Void SetLogLimits(Func<IMyTerminalBlock, Single> minGetter, Func<IMyTerminalBlock, Single> maxGetter)
```

## `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlTitleTooltip`

- assembly: `Sandbox.Common`
- kind: interface 

### Properties
```csharp
public MyStringId Title { get; set; }
public MyStringId Tooltip { get; set; }
```

## `Sandbox.ModAPI.MyAPIGateway`

- assembly: `Sandbox.Common`
- kind: class abstract sealed
- base: `System.Object`

### Fields
```csharp
public static Action<Object> GuiControlCreated
public static IMyPlayerCollection Players
public static IMyCubeBuilder CubeBuilder
public static IMyTerminalActionsHelper TerminalActionsHelper
public static IMyTerminalControls TerminalControls
public static IMyUtilities Utilities
public static IMyMultiplayer Multiplayer
public static IMyParallelTask Parallel
public static IMyPhysics Physics
public static IMyGui Gui
public static IMyPrefabManager PrefabManager
public static IMyIngameScripting IngameScripting
public static IMyInput Input
public static IMyContractSystem ContractSystem
public static IMyGridGroups GridGroups
public static IMyReflection Reflection
public static IMySpectatorTools SpectatorTools
public static IMyProjectiles Projectiles
public static IMyMissiles Missiles
public static IMyDLCs DLC
public static IMyGravityProviderSystem GravityProviderSystem
public static IMyGeneratedTextures GeneratedTextures
```

### Properties
```csharp
public static IMySession Session { get; set; }
public static IMyEntities Entities { get; set; }
```

### Methods
```csharp
public static Void Clean()
public static StringBuilder DoorBase(String name)
public static Void GetMessageBoxPointer(ref ref IntPtr pointer)
```

## `VRage.Game.Components.MySessionComponentBase`

- assembly: `VRage.Game`
- kind: class abstract
- base: `System.Object`
- interfaces: `VRage.Game.Components.Interfaces.IMyUserInputComponent`

### Constructors
```csharp
public MySessionComponentBase()
```

### Fields
```csharp
public readonly String DebugName
public readonly Int32 Priority
public readonly Type ComponentType
```

### Properties
```csharp
public MyUpdateOrder UpdateOrder { get; set; }
public MyObjectBuilderType ObjectBuilderType { get; set; }
public IMyModContext ModContext { get; set; }
public IMySession Session { get; set; }
public Boolean Loaded { get; set; }
public Boolean Initialized { get; }
public Boolean UpdateOnPause { get; set; }
public Boolean IsServerOnly { get; set; }
public Nullable<MyDefinitionId> Definition { get; set; }
public Type[] Dependencies { get; }
public Boolean IsRequiredByGame { get; }
```

### Methods
```csharp
public Void AfterLoadData()
public virtual Void BeforeStart()
public virtual Void Draw()
public virtual MyObjectBuilder_SessionComponent GetObjectBuilder()
public virtual Void HandleInput()
public virtual Void Init(MyObjectBuilder_SessionComponent sessionComponent)
public virtual Void InitFromDefinition(MySessionComponentDefinition definition)
public virtual Void LoadData()
public virtual Void SaveData()
public Void SetUpdateOrder(MyUpdateOrder order)
public virtual Void Simulate()
public virtual String ToString()
public Void UnloadDataConditional()
public virtual Void UpdateAfterSimulation()
public virtual Void UpdateBeforeSimulation()
public virtual Boolean UpdatedBeforeInit()
public virtual Void UpdatingStopped()
```

## `VRage.Game.Components.MySessionComponentDescriptor`

- assembly: `VRage.Game`
- kind: class 
- base: `System.Attribute`

### Constructors
```csharp
public MySessionComponentDescriptor(MyUpdateOrder updateOrder)
public MySessionComponentDescriptor(MyUpdateOrder updateOrder, Int32 priority)
public MySessionComponentDescriptor(MyUpdateOrder updateOrder, Int32 priority, Type obType, Type registrationType = null, Boolean serverOnly = False)
```

### Fields
```csharp
public MyUpdateOrder UpdateOrder
public Int32 Priority
public MyObjectBuilderType ObjectBuilderType
public Type ComponentType
```

### Properties
```csharp
public Boolean IsServerOnly { get; set; }
```

## `VRage.Game.Components.MyUpdateOrder`

- assembly: `VRage.Game`
- kind: enum 
- base: `System.Enum`
- interfaces: `System.IComparable`, `System.ISpanFormattable`, `System.IFormattable`, `System.IConvertible`
- values: `NoUpdate`, `BeforeSimulation`, `Simulation`, `AfterSimulation`

## `VRage.Game.ModAPI.IMyCubeBlock`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `VRage.ModAPI.IMyEntity`

### Properties
```csharp
public IMyCubeGrid CubeGrid { get; }
public MyResourceSinkComponentBase ResourceSink { get; set; }
public Dictionary<String, Single> UpgradeValues { get; }
public IMySlimBlock SlimBlock { get; }
public Boolean CheckConnectionAllowed { get; set; }
```

### Events
```csharp
public event Action<IMyCubeBlock> IsWorkingChanged
public event Action OnUpgradeValuesChanged
public event Action<IMyCubeGrid> CubeGridChanged
```

### Methods
```csharp
public abstract Void AddUpgradeValue(String upgrade, Single defaultValue)
public abstract Void CalcLocalMatrix(out ref Matrix localMatrix, out ref String currModel)
public abstract String CalculateCurrentModel(out ref Matrix orientation)
public abstract MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(Boolean copy = False)
public abstract Void Init()
public abstract Void Init(MyObjectBuilder_CubeBlock builder, IMyCubeGrid cubeGrid)
public abstract Void OnBuildSuccess(Int64 builtBy)
public abstract Void OnBuildSuccess(Int64 builtBy, Boolean instantBuild)
public abstract Void OnDestroy()
public abstract Void OnModelChange()
public abstract Void OnRegisteredToGridSystems()
public abstract Void OnRemovedByCubeBuilder()
public abstract Void OnUnregisteredFromGridSystems()
public abstract String RaycastDetectors(Vector3D worldFrom, Vector3D worldTo)
public abstract Void ReloadDetectors(Boolean refreshNetworks = True)
public abstract Int32 RemoveEffect(String effectName, Int32 exception = -1)
public abstract Void SetDamageEffect(Boolean start)
public abstract Boolean SetEffect(String effectName, Boolean stopPrevious = False)
public abstract Boolean SetEffect(String effectName, Single parameter, Boolean stopPrevious = False, Boolean ignoreParameter = False, Boolean removeSameNameEffects = False)
```

## `VRage.Game.ModAPI.IMyCubeGrid`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `VRage.ModAPI.IMyEntity`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `VRage.Game.ModAPI.Ingame.IMyCubeGrid`

### Properties
```csharp
public List<Int64> BigOwners { get; }
public List<Int64> SmallOwners { get; }
public Boolean IsRespawnGrid { get; set; }
public Boolean IsNpcSpawnedGrid { get; }
public Boolean IsStatic { get; set; }
public String CustomName { get; set; }
public Nullable<Vector3I> XSymmetryPlane { get; set; }
public Nullable<Vector3I> YSymmetryPlane { get; set; }
public Nullable<Vector3I> ZSymmetryPlane { get; set; }
public Boolean XSymmetryOdd { get; set; }
public Boolean YSymmetryOdd { get; set; }
public Boolean ZSymmetryOdd { get; set; }
public MyUpdateTiersGridPresence GridPresenceTier { get; }
public MyUpdateTiersPlayerPresence PlayerPresenceTier { get; }
public IMyGridWeaponSystem WeaponSystem { get; }
public IMyGridControlSystem ControlSystem { get; }
public IMyGridGasSystem GasSystem { get; }
public IMyGridJumpDriveSystem JumpSystem { get; }
public IMyResourceDistributorComponent ResourceDistributor { get; }
public IMyGridConveyorSystem ConveyorSystem { get; }
public Int32 RvoAgentId { get; }
public Vector3 NaturalGravity { get; }
public Boolean IsBlockTrasferInProgress { get; }
```

### Events
```csharp
public event Action<IMySlimBlock> OnBlockAdded
public event Action<IMySlimBlock> OnBlockRemoved
public event Action<IMyCubeGrid> OnBlockOwnershipChanged
public event Action<IMyCubeGrid> OnGridChanged
public event Action<IMyCubeGrid, IMyCubeGrid> OnGridSplit
public event Action<IMyCubeGrid, Boolean> OnIsStaticChanged
public event Action<IMySlimBlock> OnBlockIntegrityChanged
public event Action<IMySlimBlock, Single, Nullable<MyHitInfo>, Int64> OnGridBlockDamaged
public event Action<IMyCharacter> OnTargetLocked
public event Action<IMyCubeGrid> GridPresenceTierChanged
public event Action<IMyCubeGrid> PlayerPresenceTierChanged
public event Action<IMyCubeGrid, IMyCubeGrid> OnGridMerge
public event Action<IMyCubeGrid> OnMaxThrustChanged
public event Action<IMyCubeGrid> SpeedChanged
public event Action<IMyCubeGrid> OnNaturalGravityChanged
```

### Methods
```csharp
public abstract IMySlimBlock AddBlock(MyObjectBuilder_CubeBlock objectBuilder, Boolean testMerge)
public abstract Boolean ApplyDeformation(Single deformationOffset, Single softAreaPlanar, Single softAreaVertical, Vector3 localPos, Vector3 localNormal, MyStringHash damageType, out ref Int32 blocksDestroyedByThisCp, Single offsetThreshold = 0, Single lowerRatioLimit = 0, Int64 attackerId = 0)
public abstract Void ApplyDestructionDeformation(IMySlimBlock block)
public abstract MatrixI CalculateMergeTransform(IMyCubeGrid gridToMerge, Vector3I gridOffset)
public abstract Boolean CanAddCube(Vector3I pos)
public abstract Boolean CanAddCubes(Vector3I min, Vector3I max)
public abstract Boolean CanMergeCubes(IMyCubeGrid gridToMerge, Vector3I gridOffset)
public abstract Void ChangeGridOwnership(Int64 playerId, MyOwnershipShareModeEnum shareMode)
public abstract Void ClearSymmetries()
public abstract Void ColorBlocks(Vector3I min, Vector3I max, Vector3 newHSV)
public abstract Void FixTargetCube(out ref Vector3I cube, Vector3 fractionalGridPosition)
public abstract Void GetBlocks(List<IMySlimBlock> blocks, Func<IMySlimBlock, Boolean> collect = null)
public abstract List<IMySlimBlock> GetBlocksInsideSphere(ref ref BoundingSphereD sphere)
public abstract Vector3 GetClosestCorner(Vector3I gridPos, Vector3 position)
public abstract IMySlimBlock GetCubeBlock(Vector3I pos)
public abstract IEnumerable<T> GetFatBlocks<T>()
public abstract IMyGridGroupData GetGridGroup(GridLinkTypeEnum linkTypeEnum)
public abstract Nullable<Vector3D> GetLineIntersectionExactAll(ref ref LineD line, out ref Double distance, out ref IMySlimBlock intersectedBlock)
public abstract Boolean GetLineIntersectionExactGrid(ref ref LineD line, ref ref Vector3I position, ref ref Double distanceSquared)
public abstract Single GetMaxThrustInDirection(Direction direction)
public abstract Void InvokeDepressurizeEffect(Vector3I from, Vector3I to)
public abstract Boolean IsInSameLogicalGroupAs(IMyCubeGrid other)
public abstract Boolean IsRoomAtPositionAirtight(Vector3I vector3I)
public abstract Boolean IsSameConstructAs(IMyCubeGrid other)
public abstract Boolean IsTouchingAnyNeighbor(Vector3I min, Vector3I max)
public abstract IMyCubeGrid MergeGrid_MergeBlock(IMyCubeGrid gridToMerge, Vector3I gridOffset)
public abstract Void OnConvertToDynamic()
public abstract Nullable<Vector3I> RayCastBlocks(Vector3D worldStart, Vector3D worldEnd)
public abstract Void RayCastCells(Vector3D worldStart, Vector3D worldEnd, List<Vector3I> outHitPositions, Nullable<Vector3I> gridSizeInflate = null, Boolean havokWorld = False)
public abstract Void RazeBlock(Vector3I position)
public abstract Void RazeBlocks(List<Vector3I> locations)
public abstract Void RazeBlocks(ref ref Vector3I pos, ref ref Vector3UByte size)
public abstract Void RemoveBlock(IMySlimBlock block, Boolean updatePhysics = False)
public abstract Void RemoveDestroyedBlock(IMySlimBlock block)
public abstract Void SkinBlocks(Vector3I min, Vector3I max, Nullable<Vector3> newHSV, String newSkin)
public abstract IMyCubeGrid Split(List<IMySlimBlock> blocks, Boolean sync = True)
public abstract IMyCubeGrid SplitByPlane(PlaneD plane)
public abstract Void UpdateBlockNeighbours(IMySlimBlock block)
public abstract Void UpdateOwnership(Int64 ownerId, Boolean isFunctional)
public abstract Boolean WillRemoveBlockSplitGrid(IMySlimBlock testBlock)
```

## `VRage.Game.ModAPI.IMyGui`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public String ActiveGamePlayScreen { get; }
public IMyEntity InteractedEntity { get; }
public MyTerminalPageEnum GetCurrentScreen { get; }
public Boolean ChatEntryVisible { get; }
public Boolean IsCursorVisible { get; }
```

### Events
```csharp
public event Action<Object> GuiControlCreated
public event Action<Object> GuiControlRemoved
```

### Methods
```csharp
public abstract Void ChangeInteractedEntity(IMyEntity interactedEntity, Boolean isRemote)
public abstract Void CloseTerminalScreen()
public abstract Void ShowTerminalPage(MyTerminalPageEnum page, IMyCharacter user, IMyEntity interactedEntity = null, Boolean isRemote = False)
```

## `VRage.Game.ModAPI.IMyMultiplayer`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public Boolean MultiplayerActive { get; }
public Boolean IsServer { get; }
public UInt64 ServerId { get; }
public UInt64 MyId { get; }
public String MyName { get; }
public IMyPlayerCollection Players { get; }
```

### Methods
```csharp
public abstract Boolean IsServerPlayer(IMyNetworkClient player)
public abstract Void JoinServer(String address)
public abstract Void RegisterMessageHandler(UInt16 id, Action<Byte[]> messageHandler)
public abstract Void RegisterSecureMessageHandler(UInt16 id, Action<UInt16, Byte[], UInt64, Boolean> messageHandler)
public abstract Void SendEntitiesCreated(List<MyObjectBuilder_EntityBase> objectBuilders)
public abstract Boolean SendMessageTo(UInt16 id, Byte[] message, UInt64 recipient, Boolean reliable = True)
public abstract Boolean SendMessageToOthers(UInt16 id, Byte[] message, Boolean reliable = True)
public abstract Boolean SendMessageToServer(UInt16 id, Byte[] message, Boolean reliable = True)
public abstract Void UnregisterMessageHandler(UInt16 id, Action<Byte[]> messageHandler)
public abstract Void UnregisterSecureMessageHandler(UInt16 id, Action<UInt16, Byte[], UInt64, Boolean> messageHandler)
```

## `VRage.Game.ModAPI.IMySession`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public Single AssemblerEfficiencyMultiplier { get; }
public Single AssemblerSpeedMultiplier { get; }
public Boolean AutoHealing { get; }
public UInt32 AutoSaveInMinutes { get; }
public IMyCameraController CameraController { get; }
public Boolean CargoShipsEnabled { get; }
public Boolean ClientCanSave { get; }
public Boolean CreativeMode { get; }
public String CurrentPath { get; }
public String Description { get; set; }
public IMyCamera Camera { get; }
public Double CameraTargetDistance { get; set; }
public IMyPlayer LocalHumanPlayer { get; }
public IMyWeatherEffects WeatherEffects { get; }
public IMyConfig Config { get; }
public TimeSpan ElapsedPlayTime { get; }
public Boolean EnableCopyPaste { get; }
public MyEnvironmentHostilityEnum EnvironmentHostility { get; }
public DateTime GameDateTime { get; set; }
public Single GrinderSpeedMultiplier { get; }
public Single HackSpeedMultiplier { get; }
public Single InventoryMultiplier { get; }
public Single CharactersInventoryMultiplier { get; }
public Single BlocksInventorySizeMultiplier { get; }
public Boolean IsCameraAwaitingEntity { get; set; }
public List<ModItem> Mods { get; set; }
public Boolean IsCameraControlledObject { get; }
public Boolean IsCameraUserControlledSpectator { get; }
public Boolean IsServer { get; }
public Int16 MaxFloatingObjects { get; }
public Int16 MaxBackupSaves { get; }
public Int16 MaxPlayers { get; }
public Boolean MultiplayerAlive { get; set; }
public Boolean MultiplayerDirect { get; set; }
public Double MultiplayerLastMsg { get; set; }
public String Name { get; set; }
public Single NegativeIntegrityTotal { get; set; }
public MyOnlineModeEnum OnlineMode { get; }
public String Password { get; set; }
public Single PositiveIntegrityTotal { get; set; }
public Single RefinerySpeedMultiplier { get; }
public Boolean ShowPlayerNamesOnHud { get; }
public Boolean SurvivalMode { get; }
public Boolean ThrusterDamage { get; }
public String ThumbPath { get; }
public TimeSpan TimeOnBigShip { get; }
public TimeSpan TimeOnFoot { get; }
public TimeSpan TimeOnJetpack { get; }
public TimeSpan TimeOnSmallShip { get; }
public Boolean WeaponsEnabled { get; }
public Single WelderSpeedMultiplier { get; }
public Nullable<UInt64> WorkshopId { get; }
public IMyVoxelMaps VoxelMaps { get; }
public IMyPlayer Player { get; }
public IMyControllableEntity ControlledObject { get; }
public MyObjectBuilder_SessionSettings SessionSettings { get; }
public IMyFactionCollection Factions { get; }
public IMyDamageSystem DamageSystem { get; }
public IMyGpsCollection GPS { get; }
public BoundingBoxD WorldBoundaries { get; }
public MyPromoteLevel PromoteLevel { get; }
public Boolean HasCreativeRights { get; }
public Boolean HasAdminPrivileges { get; }
public Version Version { get; }
public IMyOxygenProviderSystem OxygenProviderSystem { get; }
public Int32 GameplayFrameCounter { get; }
public Int32 TotalBotLimit { get; }
```

### Events
```csharp
public event Action OnSessionReady
public event Action OnSessionLoading
```

### Methods
```csharp
public abstract Void BeforeStartComponents()
public abstract Void Draw()
public abstract Void GameOver()
public abstract Void GameOver(Nullable<MyStringId> customMessage)
public abstract MyObjectBuilder_Checkpoint GetCheckpoint(String saveName)
public abstract MySessionComponentBase GetComponentByInterfaceType<T>()
public abstract MyObjectBuilder_Sector GetSector()
public abstract MyPromoteLevel GetUserPromoteLevel(UInt64 steamId)
public abstract Dictionary<String, Byte[]> GetVoxelMapsArray()
public abstract MyObjectBuilder_World GetWorld()
public abstract Boolean IsPausable()
public abstract Boolean IsUserAdmin(UInt64 steamId)
public abstract Boolean IsUserIgnorePCULimit(UInt64 steamId)
public abstract Boolean IsUserIgnoreSafeZones(UInt64 steamId)
public abstract Boolean IsUserInvulnerable(UInt64 steamId)
public abstract Boolean IsUserKeepOriginalOwnershipOnPaste(UInt64 steamId)
public abstract Boolean IsUserPromoted(UInt64 steamId)
public abstract Boolean IsUserShowAllPlayers(UInt64 steamId)
public abstract Boolean IsUserUntargetable(UInt64 steamId)
public abstract Boolean IsUserUseAllTerminals(UInt64 steamId)
public abstract Void RegisterComponent(MySessionComponentBase component, MyUpdateOrder updateOrder, Int32 priority)
public abstract Boolean Save(String customSaveName = null)
public abstract Void SetAsNotReady()
public abstract Void SetCameraController(MyCameraControllerEnum cameraControllerEnum, IMyEntity cameraEntity = null, Nullable<Vector3D> position = null)
public abstract Void SetComponentUpdateOrder(MySessionComponentBase component, MyUpdateOrder order)
public abstract Boolean TryGetAdminSettings(UInt64 steamId, out ref MyAdminSettingsEnum adminSettings)
public abstract Boolean TryGetComponentByInterfaceType<T>(out ref T sessionComponent)
public abstract Void Unload()
public abstract Void UnloadDataComponents()
public abstract Void UnloadMultiplayer()
public abstract Void UnregisterComponent(MySessionComponentBase component)
public abstract Void Update(MyTimeSpan time)
public abstract Void UpdateComponents()
```

## `VRage.Game.ModAPI.IMySlimBlock`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.Ingame.IMySlimBlock`, `VRage.Game.ModAPI.Interfaces.IMyDestroyableObject`, `VRage.Game.ModAPI.Interfaces.IMyDecalProxy`

### Properties
```csharp
public Single BlockGeneralDamageModifier { get; set; }
public IMyCubeBlock FatBlock { get; }
public IMyCubeGrid CubeGrid { get; }
public MyDefinitionBase BlockDefinition { get; }
public Vector3I Max { get; }
public Vector3I Min { get; }
public MyBlockOrientation Orientation { get; }
public List<IMySlimBlock> Neighbours { get; }
public Single Dithering { get; set; }
public Int64 BuiltBy { get; }
public IMyComponentStack ComponentStack { get; }
public Boolean IsMovedBySplit { get; }
```

### Methods
```csharp
public abstract Void AddNeighbours()
public abstract Void ApplyAccumulatedDamage(Boolean addDirtyParts = True)
public abstract String CalculateCurrentModel(out ref Matrix orientation)
public abstract Boolean CanContinueBuild(IMyInventory sourceInventory)
public abstract Void ClearConstructionStockpile(IMyInventory outputInventory)
public abstract Void ComputeScaledCenter(out ref Vector3D scaledCenter)
public abstract Void ComputeScaledHalfExtents(out ref Vector3 scaledHalfExtents)
public abstract Void ComputeWorldCenter(out ref Vector3D worldCenter)
public abstract Void DecreaseMountLevel(Single grinderAmount, IMyInventory outputInventory, Boolean useDefaultDeconstructEfficiency = False)
public abstract Void FixBones(Single oldDamage, Single maxAllowedBoneMovement)
public abstract Void FullyDismount(IMyInventory outputInventory)
public abstract Vector3 GetColorMask()
public abstract Int32 GetConstructionStockpileItemAmount(MyDefinitionId id)
public abstract MyObjectBuilder_CubeBlock GetCopyObjectBuilder()
public abstract Void GetNeighbours(ICollection<IMySlimBlock> collection)
public abstract MyObjectBuilder_CubeBlock GetObjectBuilder(Boolean copy = False)
public abstract Void GetWorldBoundingBox(out ref BoundingBoxD aabb, Boolean useAABBFromBlockCubes = False)
public abstract Void IncreaseMountLevel(Single welderMountAmount, Int64 welderOwnerPlayerId, IMyInventory outputInventory = null, Single maxAllowedBoneMovement = 0, Boolean isHelping = False, MyOwnershipShareModeEnum share = Faction)
public abstract Void InitOrientation(MyBlockOrientation orientation)
public abstract Void InitOrientation(Direction Forward, Direction Up)
public abstract Void InitOrientation(ref ref Vector3I forward, ref ref Vector3I up)
public abstract Void MoveItemsFromConstructionStockpile(IMyInventory toInventory, MyItemFlags flags = None)
public abstract Void MoveItemsToConstructionStockpile(IMyInventory fromInventory)
public abstract Void PlayConstructionSound(MyIntegrityChangeEnum integrityChangeType, Boolean deconstruction = False)
public abstract Void RemoveNeighbours()
public abstract Void SetToConstructionSite()
public abstract Void SpawnConstructionStockpile()
public abstract Void SpawnFirstItemInConstructionStockpile()
public abstract Void UpdateVisual()
```

## `VRage.Game.ModAPI.IMyUtilities`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public IMyConfigDedicated ConfigDedicated { get; }
public IMyGamePaths GamePaths { get; }
public Boolean IsDedicated { get; }
```

### Events
```csharp
public event MessageEnteredDel MessageEntered
public event MessageEnteredSenderDel MessageEnteredSender
public event Action<UInt64, String> MessageRecieved
```

### Methods
```csharp
public abstract IMyHudNotification CreateNotification(String message, Int32 disappearTimeMs = 2000, String font = White)
public abstract Void DeleteFileInGlobalStorage(String file)
public abstract Void DeleteFileInLocalStorage(String file, Type callingType)
public abstract Void DeleteFileInWorldStorage(String file, Type callingType)
public abstract Boolean FileExistsInGameContent(String file)
public abstract Boolean FileExistsInGlobalStorage(String file)
public abstract Boolean FileExistsInLocalStorage(String file, Type callingType)
public abstract Boolean FileExistsInModLocation(String file, ModItem modItem)
public abstract Boolean FileExistsInWorldStorage(String file, Type callingType)
public abstract IMyHudObjectiveLine GetObjectiveLine()
public abstract String GetTypeName(Type type)
public abstract Boolean GetVariable<T>(String name, out ref T value)
public abstract Void InvokeOnGameThread(Action action, String invokerName = ModAPI, Int32 StartAt = -1, Int32 RepeatTimes = 0)
public abstract BinaryReader ReadBinaryFileInGameContent(String file)
public abstract BinaryReader ReadBinaryFileInGlobalStorage(String file)
public abstract BinaryReader ReadBinaryFileInLocalStorage(String file, Type callingType)
public abstract BinaryReader ReadBinaryFileInModLocation(String file, ModItem modItem)
public abstract BinaryReader ReadBinaryFileInWorldStorage(String file, Type callingType)
public abstract TextReader ReadFileInGameContent(String file)
public abstract TextReader ReadFileInGlobalStorage(String file)
public abstract TextReader ReadFileInLocalStorage(String file, Type callingType)
public abstract TextReader ReadFileInModLocation(String file, ModItem modItem)
public abstract TextReader ReadFileInWorldStorage(String file, Type callingType)
public abstract Void RegisterMessageHandler(Int64 id, Action<Object> messageHandler)
public abstract Boolean RemoveVariable(String name)
public abstract Void SendMessage(String messageText)
public abstract Void SendModMessage(Int64 id, Object payload)
public abstract T SerializeFromBinary<T>(Byte[] data)
public abstract T SerializeFromXML<T>(String buffer)
public abstract Byte[] SerializeToBinary<T>(T obj)
public abstract String SerializeToXML<T>(T objToSerialize)
public abstract Void SetVariable<T>(String name, T value)
public abstract Void ShowMessage(String sender, String messageText)
public abstract Void ShowMissionScreen(String screenTitle = null, String currentObjectivePrefix = null, String currentObjective = null, String screenDescription = null, Action<ResultEnum> callback = null, String okButtonCaption = null)
public abstract Void ShowNotification(String message, Int32 disappearTimeMs = 2000, String font = White)
public abstract Void UnregisterMessageHandler(Int64 id, Action<Object> messageHandler)
public abstract BinaryWriter WriteBinaryFileInGlobalStorage(String file)
public abstract BinaryWriter WriteBinaryFileInLocalStorage(String file, Type callingType)
public abstract BinaryWriter WriteBinaryFileInWorldStorage(String file, Type callingType)
public abstract TextWriter WriteFileInGlobalStorage(String file)
public abstract TextWriter WriteFileInLocalStorage(String file, Type callingType)
public abstract TextWriter WriteFileInWorldStorage(String file, Type callingType)
```

## `VRage.Game.ModAPI.Ingame.IMyCubeBlock`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.Ingame.IMyEntity`

### Properties
```csharp
public SerializableDefinitionId BlockDefinition { get; }
public IMyCubeGrid CubeGrid { get; }
public String DefinitionDisplayNameText { get; }
public Single DisassembleRatio { get; }
public String DisplayNameText { get; }
public Boolean IsBeingHacked { get; }
public Boolean IsFunctional { get; }
public Boolean IsWorking { get; }
public Vector3I Max { get; }
public Single Mass { get; }
public Vector3I Min { get; }
public Int32 NumberInGrid { get; }
public MyBlockOrientation Orientation { get; }
public Int64 OwnerId { get; }
public Vector3I Position { get; }
```

### Methods
```csharp
public abstract String GetOwnerFactionTag()
public abstract MyRelationsBetweenPlayerAndBlock GetPlayerRelationToOwner()
public abstract MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(Int64 playerId, MyRelationsBetweenPlayerAndBlock defaultNoUser = NoOwnership)
public abstract Void UpdateIsWorking()
public abstract Void UpdateVisual()
```

## `VRage.Game.ModAPI.Ingame.IMyCubeGrid`

- assembly: `VRage.Game`
- kind: interface 
- interfaces: `VRage.Game.ModAPI.Ingame.IMyEntity`

### Properties
```csharp
public String CustomName { get; set; }
public Single GridSize { get; }
public MyCubeSize GridSizeEnum { get; }
public Boolean IsStatic { get; }
public Vector3I Max { get; }
public Vector3I Min { get; }
public Vector3 LinearVelocity { get; }
public Single Speed { get; }
```

### Methods
```csharp
public abstract Boolean CubeExists(Vector3I pos)
public abstract IMySlimBlock GetCubeBlock(Vector3I pos)
public abstract Vector3D GridIntegerToWorld(Vector3I gridCoords)
public abstract Boolean IsSameConstructAs(IMyCubeGrid other)
public abstract Vector3I WorldToGridInteger(Vector3D coords)
```

## `VRage.Game.ModAPI.Ingame.IMySlimBlock`

- assembly: `VRage.Game`
- kind: interface 

### Properties
```csharp
public SerializableDefinitionId BlockDefinition { get; }
public Single AccumulatedDamage { get; }
public Single BuildIntegrity { get; }
public Single BuildLevelRatio { get; }
public Single CurrentDamage { get; }
public Single DamageRatio { get; }
public IMyCubeBlock FatBlock { get; }
public Boolean HasDeformation { get; }
public Boolean IsDestroyed { get; }
public Boolean IsFullIntegrity { get; }
public Boolean IsFullyDismounted { get; }
public Single MaxDeformation { get; }
public Single MaxIntegrity { get; }
public Single Mass { get; }
public Int64 OwnerId { get; }
public Boolean ShowParts { get; }
public Boolean StockpileAllocated { get; }
public Boolean StockpileEmpty { get; }
public Vector3I Position { get; }
public IMyCubeGrid CubeGrid { get; }
public Vector3 ColorMaskHSV { get; }
public MyStringHash SkinSubtypeId { get; }
```

### Methods
```csharp
public abstract Void GetMissingComponents(Dictionary<String, Int32> addToDictionary)
```


