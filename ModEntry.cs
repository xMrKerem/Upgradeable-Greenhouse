using Microsoft.Xna.Framework;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Objects;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using xTile;

namespace GreenhouseUpgrade;

public class ModEntry : Mod
{
    private const string SaveDataKey = "xMrAydin.UpgradeableGreenhouse.SaveData";
    private const string GreenhouseLocationName = "Greenhouse";
    private const string GreenhouseMapAssetName = "Maps/Greenhouse";
    private const string ObjectDataAssetName = "Data/Objects";
    private const string UpgradeIconItemId = "xMrAydin.UpgradeableGreenhouse.GreenhouseIcon";
    private const string UpgradeIconQualifiedItemId = "(O)" + UpgradeIconItemId;
    private const string RobinUpgradeResponseKey = "xMrAydinGreenhouseUpgrade";
    private const string ConfirmUpgradeQuestionKey = "xMrAydinGreenhouseUpgradeConfirm";
    private const string ConfirmUpgradeYesKey = "xMrAydinGreenhouseUpgradeYes";
    private const string ConfirmUpgradeNoKey = "xMrAydinGreenhouseUpgradeNo";
    private const int MaxGreenhouseLevel = 3;
    private const int MaxPurchasableGreenhouseLevel = 2;
    private const int EdgeBandTiles = 4;
    private const int UpgradeDays = 3;

    private static ModEntry? Instance;

    private ModData Data = new ModData();
    private Harmony? Harmony;

    public override void Entry(IModHelper helper)
    {
        Instance = this;

        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.Player.Warped += this.OnWarped;

        this.ApplyHarmonyPatches();

        this.Monitor.Log("Upgradeable Greenhouse loaded.", LogLevel.Debug);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Data = this.Helper.Data.ReadSaveData<ModData>(SaveDataKey) ?? new ModData();
        this.Data.GreenhouseLevel = Math.Clamp(this.Data.GreenhouseLevel, 0, MaxGreenhouseLevel);
        this.Data.DaysUntilUpgrade = Math.Max(0, this.Data.DaysUntilUpgrade);

        this.RebuildGreenhouseLocation(movePlayerToEntry: false, shiftPlacedContent: false);

        this.Monitor.Log($"Greenhouse level for this save: {this.Data.GreenhouseLevel}", LogLevel.Info);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (this.Data.DaysUntilUpgrade <= 0)
            return;

        this.Data.DaysUntilUpgrade--;

        if (this.Data.DaysUntilUpgrade > 0)
        {
            this.SaveData();
            return;
        }

        int previousLevel = this.Data.GreenhouseLevel;
        if (previousLevel >= MaxPurchasableGreenhouseLevel)
        {
            this.Data.ShowUpgradeFinishedMessage = false;
            this.SaveData();
            return;
        }

        this.Data.GreenhouseLevel = Math.Clamp(this.Data.GreenhouseLevel + 1, 0, MaxPurchasableGreenhouseLevel);
        this.Data.ShowUpgradeFinishedMessage = false;
        this.SaveData();
        this.RebuildGreenhouseLocation(movePlayerToEntry: false, shiftPlacedContent: previousLevel != this.Data.GreenhouseLevel);

        this.ShowGreenhouseFinishedHudMessage();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.SaveData();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Data = new ModData();
        this.InvalidateGreenhouseMap();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ObjectDataAssetName))
        {
            e.Edit(asset =>
            {
                asset.AsDictionary<string, ObjectData>().Data[UpgradeIconItemId] = new ObjectData
                {
                    Name = "Greenhouse",
                    DisplayName = this.Helper.Translation.Get("menu.greenhouse"),
                    Description = this.Helper.Translation.Get("menu.greenhouse"),
                    Type = "Basic",
                    Category = -999,
                    Price = 0,
                    Texture = "Buildings/Greenhouse",
                    SpriteIndex = 0,
                    Edibility = -300,
                    CanBeGivenAsGift = false,
                    CanBeTrashed = false,
                    ExcludeFromShippingCollection = true,
                    ExcludeFromRandomSale = true
                };
            });
            return;
        }

        if (!e.NameWithoutLocale.IsEquivalentTo(GreenhouseMapAssetName))
            return;

        string? mapPath = this.GetMapPath(this.Data.GreenhouseLevel);
        if (mapPath == null)
            return;

        e.LoadFromModFile<Map>(mapPath, AssetLoadPriority.Exclusive);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer || !this.IsGreenhouse(e.NewLocation))
            return;

        if (this.Data.GreenhouseLevel == 0)
            return;

        this.MovePlayerToEntry();
    }

    private void ApplyHarmonyPatches()
    {
        this.Harmony = new Harmony(this.ModManifest.UniqueID);

        System.Reflection.MethodInfo? createQuestionDialogue = AccessTools.Method(
            typeof(GameLocation),
            "createQuestionDialogue",
            new[] { typeof(string), typeof(Response[]), typeof(string) }
        );
        System.Reflection.MethodInfo? answerDialogueAction = AccessTools.Method(
            typeof(GameLocation),
            "answerDialogueAction"
        );
        System.Reflection.MethodInfo? drawDialogue = AccessTools.Method(
            typeof(Game1),
            nameof(Game1.DrawDialogue),
            new[] { typeof(Dialogue) }
        );

        if (createQuestionDialogue != null)
        {
            this.Harmony.Patch(
                createQuestionDialogue,
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeCreateQuestionDialogue))
            );
        }
        else
        {
            this.Monitor.Log("Robin greenhouse upgrade option patch could not find GameLocation.createQuestionDialogue.", LogLevel.Warn);
        }

        if (answerDialogueAction != null)
        {
            this.Harmony.Patch(
                answerDialogueAction,
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeAnswerDialogueAction))
            );
        }
        else
        {
            this.Monitor.Log("Robin greenhouse upgrade answer patch could not find GameLocation.answerDialogueAction.", LogLevel.Warn);
        }

        if (drawDialogue != null)
        {
            this.Harmony.Patch(
                drawDialogue,
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(BeforeDrawDialogue))
            );
        }
    }

    private static void BeforeCreateQuestionDialogue(string __0, ref Response[] __1, string __2)
    {
        Instance?.AddGreenhouseUpgradeResponse(ref __1, __2);
    }

    private static bool BeforeAnswerDialogueAction(string __0, ref bool __result)
    {
        if (Instance?.HandleGreenhouseUpgradeDialogueAnswer(__0) != true)
            return true;

        __result = true;
        return false;
    }

    private static void BeforeDrawDialogue(Dialogue __0)
    {
        Instance?.AddGreenhouseUpgradeResponse(__0);
    }

    private void AddGreenhouseUpgradeResponse(ref Response[] answerChoices, string dialogKey)
    {
        if (!this.ShouldAddGreenhouseUpgradeResponse(answerChoices, dialogKey))
            return;

        Response upgradeResponse = new Response(RobinUpgradeResponseKey, this.Helper.Translation.Get("menu.upgrade_greenhouse"));

        if (answerChoices.Length > 0 && this.IsLeaveResponse(answerChoices[^1]))
            answerChoices = answerChoices[..^1].Append(upgradeResponse).Append(answerChoices[^1]).ToArray();
        else
            answerChoices = answerChoices.Append(upgradeResponse).ToArray();
    }

    private void AddGreenhouseUpgradeResponse(Dialogue dialogue)
    {
        System.Reflection.FieldInfo? responsesField = AccessTools.Field(typeof(Dialogue), "playerResponses");
        if (responsesField?.GetValue(dialogue) is not IList<Response> rawResponses || rawResponses.Count == 0)
            return;

        List<Response> responses = rawResponses is List<Response> responseList
            ? responseList
            : rawResponses.ToList();

        Response[] answerChoices = responses.ToArray();
        if (!this.ShouldAddGreenhouseUpgradeResponse(answerChoices, dialogue.TranslationKey ?? string.Empty))
            return;

        Response upgradeResponse = new Response(RobinUpgradeResponseKey, this.Helper.Translation.Get("menu.upgrade_greenhouse"));

        if (responses.Count > 0 && this.IsLeaveResponse(responses[^1]))
            responses.Insert(responses.Count - 1, upgradeResponse);
        else
            responses.Add(upgradeResponse);

        if (!ReferenceEquals(responses, rawResponses))
            responsesField.SetValue(dialogue, responses);
    }

    private bool ShouldAddGreenhouseUpgradeResponse(Response[] answerChoices, string dialogKey)
    {
        if (!Context.IsWorldReady || this.Data.DaysUntilUpgrade > 0 || this.Data.GreenhouseLevel >= MaxPurchasableGreenhouseLevel)
            return false;

        if (!this.IsRobinDeskDialogue(answerChoices, dialogKey))
            return false;

        return !answerChoices.Any(response => string.Equals(response.responseKey, RobinUpgradeResponseKey, StringComparison.Ordinal));
    }

    private bool IsRobinDeskDialogue(Response[] answerChoices, string dialogKey)
    {
        string locationName = Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? string.Empty;
        if (!string.Equals(locationName, "ScienceHouse", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(dialogKey, ConfirmUpgradeQuestionKey, StringComparison.Ordinal))
            return false;

        bool speakerLooksRight = string.Equals(Game1.currentSpeaker?.Name, "Robin", StringComparison.OrdinalIgnoreCase);
        bool keyLooksRight = dialogKey.Contains("carpenter", StringComparison.OrdinalIgnoreCase)
            || dialogKey.Contains("robin", StringComparison.OrdinalIgnoreCase);
        bool choicesLookRight = answerChoices.Any(this.IsRobinShopResponse)
            || answerChoices.Any(this.IsConstructResponse)
            || answerChoices.Any(this.IsCarpenterResponse);

        return speakerLooksRight || keyLooksRight || choicesLookRight || answerChoices.Length >= 3;
    }

    private bool IsRobinShopResponse(Response response)
    {
        return response.responseKey.Contains("shop", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("supplies", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsConstructResponse(Response response)
    {
        return response.responseKey.Contains("construct", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("build", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("building", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCarpenterResponse(Response response)
    {
        return response.responseKey.Contains("carpenter", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("robin", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("upgrade", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLeaveResponse(Response response)
    {
        return response.responseKey.Contains("leave", StringComparison.OrdinalIgnoreCase)
               || response.responseKey.Contains("exit", StringComparison.OrdinalIgnoreCase);
    }

    private bool HandleGreenhouseUpgradeDialogueAnswer(string questionAndAnswer)
    {
        if (questionAndAnswer.Contains(ConfirmUpgradeYesKey, StringComparison.Ordinal))
        {
            this.StartGreenhouseUpgrade(Game1.player);
            return true;
        }

        if (questionAndAnswer.Contains(ConfirmUpgradeNoKey, StringComparison.Ordinal))
            return true;

        if (!questionAndAnswer.Contains(RobinUpgradeResponseKey, StringComparison.Ordinal))
            return false;

        this.ShowGreenhouseUpgradeConfirmation();
        return true;
    }

    private void ShowGreenhouseUpgradeConfirmation()
    {
        if (!Context.IsWorldReady)
            return;

        if (this.Data.DaysUntilUpgrade > 0 || this.Data.GreenhouseLevel >= MaxPurchasableGreenhouseLevel)
            return;

        Game1.currentLocation.createQuestionDialogue(
            this.GetUpgradeQuestion(),
            new[]
            {
                new Response(ConfirmUpgradeYesKey, this.Helper.Translation.Get("menu.yes")),
                new Response(ConfirmUpgradeNoKey, this.Helper.Translation.Get("menu.no"))
            },
            ConfirmUpgradeQuestionKey
        );
    }

    private void StartGreenhouseUpgrade(Farmer who)
    {
        int targetLevel = this.Data.GreenhouseLevel + 1;
        if (this.Data.DaysUntilUpgrade > 0 || targetLevel > MaxPurchasableGreenhouseLevel)
            return;

        UpgradeCost cost = this.GetUpgradeCost(targetLevel);

        if (who.Money < cost.Gold)
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("error.not_enough_gold")));
            return;
        }

        if (!this.HasUpgradeMaterials(who, cost))
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("error.not_enough_materials")));
            return;
        }

        who.Money -= cost.Gold;
        this.RemoveUpgradeMaterials(who, cost);

        this.Data.DaysUntilUpgrade = UpgradeDays;
        this.Data.ShowUpgradeFinishedMessage = true;
        this.SaveData();

        this.ShowRobinDialogue(this.Helper.Translation.Get("success.upgrade_started"));
    }

    private void ShowRobinDialogue(string message)
    {
        NPC? robin = Game1.getCharacterFromName("Robin");
        if (robin == null)
        {
            Game1.addHUDMessage(new HUDMessage(message));
            return;
        }

        Game1.DrawDialogue(new Dialogue(robin, $"{this.ModManifest.UniqueID}:greenhouse_upgrade_started", message));
    }

    private void ShowGreenhouseFinishedHudMessage()
    {
        HUDMessage message = new HUDMessage(this.Helper.Translation.Get("success.upgrade_finished"))
        {
            messageSubject = ItemRegistry.Create(UpgradeIconQualifiedItemId, 1, 0, allowNull: true),
            noIcon = false,
            type = $"{this.ModManifest.UniqueID}/GreenhouseFinished"
        };

        Game1.addHUDMessage(message);
    }

    private void RebuildGreenhouseLocation(bool movePlayerToEntry, bool shiftPlacedContent)
    {
        if (!Context.IsWorldReady)
            return;

        GameLocation? greenhouse = this.FindGreenhouseLocation();
        if (greenhouse == null)
            return;

        bool playerWasInside = this.IsGreenhouse(Game1.currentLocation);
        Point oldMapSize = this.GetMapSize(greenhouse);

        this.InvalidateGreenhouseMap();
        greenhouse.reloadMap();
        greenhouse.updateWarps();

        if (shiftPlacedContent)
            this.ShiftPlacedContentForResize(greenhouse, oldMapSize, this.GetMapSize(greenhouse));

        if (playerWasInside && movePlayerToEntry)
            this.MovePlayerToEntry();
    }

    private GameLocation? FindGreenhouseLocation()
    {
        foreach (GameLocation location in Game1.locations)
        {
            if (this.IsGreenhouse(location))
                return location;
        }

        return null;
    }

    private Point GetMapSize(GameLocation location)
    {
        xTile.Layers.Layer layer = location.Map.GetLayer("Back");
        return new Point(layer.LayerWidth, layer.LayerHeight);
    }

    private void ShiftPlacedContentForResize(GameLocation location, Point oldSize, Point newSize)
    {
        int dx = newSize.X - oldSize.X;
        int dy = newSize.Y - oldSize.Y;

        if (dx == 0 && dy == 0)
            return;

        int rightBandStart = oldSize.X - EdgeBandTiles;
        int bottomBandStart = oldSize.Y - EdgeBandTiles;

        if (dx != 0)
            location.shiftContents(dx, 0, (tile, content) => this.ShouldShiftContent(tile, content, rightBandStart, checkRightBand: true));

        if (dy != 0)
            location.shiftContents(0, dy, (tile, content) => this.ShouldShiftContent(tile, content, bottomBandStart, checkRightBand: false));
    }

    private bool ShouldShiftContent(Vector2 tile, object content, int bandStart, bool checkRightBand)
    {
        if (content is HoeDirt)
            return false;

        Rectangle bounds = this.GetContentTileBounds(tile, content);
        return checkRightBand
            ? bounds.Right > bandStart
            : bounds.Bottom > bandStart;
    }

    private Rectangle GetContentTileBounds(Vector2 tile, object content)
    {
        if (content is Furniture furniture)
            return this.GetTileBounds(furniture.GetBoundingBoxAt((int)tile.X, (int)tile.Y));

        if (content is StardewValley.Object obj)
            return this.GetTileBounds(obj.GetBoundingBox());

        if (content is LargeTerrainFeature largeFeature)
            return new Rectangle((int)largeFeature.Tile.X, (int)largeFeature.Tile.Y, 1, 1);

        if (content is ResourceClump clump)
            return new Rectangle((int)clump.Tile.X, (int)clump.Tile.Y, 1, 1);

        return new Rectangle((int)tile.X, (int)tile.Y, 1, 1);
    }

    private Rectangle GetTileBounds(Rectangle pixelBounds)
    {
        int left = pixelBounds.Left / Game1.tileSize;
        int top = pixelBounds.Top / Game1.tileSize;
        int right = (pixelBounds.Right - 1) / Game1.tileSize;
        int bottom = (pixelBounds.Bottom - 1) / Game1.tileSize;

        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private bool IsGreenhouse(GameLocation? location)
    {
        return location != null
            && (
                string.Equals(location.Name, GreenhouseLocationName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(location.NameOrUniqueName, GreenhouseLocationName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private string GetUpgradeQuestion()
    {
        return this.Data.GreenhouseLevel switch
        {
            0 => this.Helper.Translation.Get("question.level1"),
            1 => this.Helper.Translation.Get("question.level2"),
            //Level 3 upgrade is temporarily disabled.
            //2 => this.Helper.Translation.Get("question.level3"),
            _ => this.Helper.Translation.Get("menu.upgrade_greenhouse")
        };
    }

    private UpgradeCost GetUpgradeCost(int targetLevel)
    {
        return targetLevel switch
        {
            1 => new UpgradeCost(30000, "388", 250, "390", 200),
            2 => new UpgradeCost(50000, "388", 300, "709", 100),
            //Level 3 upgrade is temporarily disabled.
            //3 => new UpgradeCost(80000, "390", 500, "709", 150),
            _ => throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, "Invalid greenhouse upgrade level.")
        };
    }

    private bool HasUpgradeMaterials(Farmer who, UpgradeCost cost)
    {
        return who.Items.CountId(cost.FirstItemId) >= cost.FirstItemCount
            && who.Items.CountId(cost.SecondItemId) >= cost.SecondItemCount;
    }

    private void RemoveUpgradeMaterials(Farmer who, UpgradeCost cost)
    {
        who.Items.ReduceId(cost.FirstItemId, cost.FirstItemCount);
        who.Items.ReduceId(cost.SecondItemId, cost.SecondItemCount);
    }

    private void MovePlayerToEntry()
    {
        Vector2 entryTile = this.Data.GreenhouseLevel switch
        {
            1 => new Vector2(11f, 28f),
            2 or 3 => new Vector2(13.5f, 33f),
            _ => new Vector2(10f, 24f)
        };

        Game1.player.Position = entryTile * 64f;
        Game1.player.currentLocation = Game1.currentLocation;
        Game1.player.Halt();
        Game1.player.faceDirection(2);
    }

    private string? GetMapPath(int level)
    {
        return level switch
        {
            1 => "assets/Greenhouse_Stage1.tmx",
            2 => "assets/Greenhouse_Stage2.tmx",
            3 => "assets/Greenhouse_Stage3.tmx",
            _ => null
        };
    }

    private void SaveData()
    {
        this.Helper.Data.WriteSaveData(SaveDataKey, this.Data);
    }

    private void InvalidateGreenhouseMap()
    {
        this.Helper.GameContent.InvalidateCache(asset => asset.NameWithoutLocale.IsEquivalentTo(GreenhouseMapAssetName));
    }

    private readonly record struct UpgradeCost(
        int Gold,
        string FirstItemId,
        int FirstItemCount,
        string SecondItemId,
        int SecondItemCount
    );
}