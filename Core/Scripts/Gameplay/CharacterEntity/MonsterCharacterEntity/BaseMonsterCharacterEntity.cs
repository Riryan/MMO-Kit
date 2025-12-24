using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;
using LiteNetLibManager;
using LiteNetLib;
using Cysharp.Threading.Tasks;

namespace MultiplayerARPG
{
    public abstract partial class BaseMonsterCharacterEntity : BaseCharacterEntity
    {
        public const float TELEPORT_TO_SUMMONER_DELAY = 5f;

        public readonly Dictionary<BaseCharacterEntity, ReceivedDamageRecord> receivedDamageRecords = new Dictionary<BaseCharacterEntity, ReceivedDamageRecord>();

        [Category("Character Settings")]
        [SerializeField]
        [FormerlySerializedAs("monsterCharacter")]
        protected MonsterCharacter characterDatabase;
        [SerializeField] protected bool isOverrideCharacteristic;
        [SerializeField] protected MonsterCharacteristic overrideCharacteristic;
        [SerializeField] protected Faction faction;
        [SerializeField] protected float destroyDelay = 2f;
        [SerializeField] protected float destroyRespawnDelay = 5f;

        [Category("Sync Fields")]
        [SerializeField] protected SyncFieldUInt summonerObjectId = new SyncFieldUInt();
        [SerializeField] protected SyncFieldByte summonType = new SyncFieldByte();

        // ---------- Think-gate / duty-cycling ----------
        private const float RECENT_DAMAGE_WINDOW = 2.5f;
        private const float ALERT_LINGER_AFTER_LOS = 2.0f;

        private float _nextThinkAt;
        private float _thinkJitter;
        private ThinkTier _thinkTier;
        private float _nextEngagedBypassAt;
        private float _alertUntil;
        private bool _hadTargetLastTick;

        private enum ThinkTier { Idle, Alert, Engaged }

        private static int s_aiLastFrameCount;
        private static int s_aiTokensUsed;
        public static int s_aiPercentPerFrame = 12;
        private static int s_aiTokensThisFrame;
        private static int s_aiMonsterCount;

        // ---------- Inline XP coalescer ----------
        private class _XpBucket { public BasePlayerCharacterEntity P; public int Exp; public RewardGivenType Type; public int SrcMin, SrcMax; public float NextFlushAt; }
        private static readonly Dictionary<string, _XpBucket> _xp = new Dictionary<string, _XpBucket>(256);
        private static readonly List<string> _xpKeys = new List<string>(256);
        private const float _XP_WINDOW = 0.10f;
        private static int _xpLastFrame;

        private static void _XpEnqueue(BasePlayerCharacterEntity p, int finalXp, RewardGivenType type, int srcMin, int srcMax)
        {
            if (p == null || finalXp <= 0) return;
            if (!_xp.TryGetValue(p.Id, out var b))
            {
                b = new _XpBucket { P = p, NextFlushAt = Time.unscaledTime + _XP_WINDOW };
                _xp[p.Id] = b;
            }
            b.Exp += finalXp;
            b.Type = type; b.SrcMin = srcMin; b.SrcMax = srcMax;
            if (Time.unscaledTime >= b.NextFlushAt) _XpFlush(b);
        }

        private static void _XpTick()
        {
            if (Time.frameCount == _xpLastFrame) return;
            _xpLastFrame = Time.frameCount;
            if (_xp.Count == 0) return;
            float now = Time.unscaledTime;
            _xpKeys.Clear(); _xpKeys.AddRange(_xp.Keys);
            int flushed = 0, maxPerFrame = 64;
            for (int i = 0; i < _xpKeys.Count && flushed < maxPerFrame; ++i)
            {
                if (!_xp.TryGetValue(_xpKeys[i], out var b)) continue;
                if (now >= b.NextFlushAt) { _XpFlush(b); ++flushed; }
            }
        }

        private static void _XpFlush(_XpBucket b)
        {
            if (b == null || b.P == null || b.Exp <= 0) { if (b?.P != null) _xp.Remove(b.P.Id); return; }
            b.P.RewardExp(b.Exp, 1f, b.Type, b.SrcMin, b.SrcMax);
            _xp.Remove(b.P.Id);
        }

        // ---------- Properties ----------
        public override string EntityTitle { get { string title = base.EntityTitle; return !string.IsNullOrEmpty(title) ? title : characterDatabase.Title; } }

        private BaseCharacterEntity summoner;
        public BaseCharacterEntity Summoner
        {
            get
            {
                if (summoner == null)
                {
                    if (Manager.Assets.TryGetSpawnedObject(summonerObjectId.Value, out LiteNetLibIdentity identity))
                        summoner = identity.GetComponent<BaseCharacterEntity>();
                }
                return summoner;
            }
            protected set { summoner = value; if (IsServer) summonerObjectId.Value = summoner != null ? summoner.ObjectId : 0; }
        }

        public SummonType SummonType { get { return (SummonType)summonType.Value; } protected set { summonType.Value = (byte)value; } }
        public bool IsSummoned { get { return SummonType != SummonType.None; } }
        public bool IsSummonedAndSummonerExisted { get { return IsSummoned && Summoner != null; } }

        public GameSpawnArea<BaseMonsterCharacterEntity> SpawnArea { get; protected set; }
        public BaseMonsterCharacterEntity SpawnPrefab { get; protected set; }
        public int SpawnLevel { get; protected set; }
        public Vector3 SpawnPosition { get; protected set; }
        public MonsterCharacter CharacterDatabase { get { return characterDatabase; } set { characterDatabase = value; } }
        public Faction Faction { get { return faction; } set { faction = value; } }
        public bool IsOverrideCharacteristic { get { return isOverrideCharacteristic; } set { isOverrideCharacteristic = value; } }
        public MonsterCharacteristic OverrideCharacteristic { get { return overrideCharacteristic; } set { overrideCharacteristic = value; } }
        public MonsterCharacteristic Characteristic { get { return IsOverrideCharacteristic ? OverrideCharacteristic : CharacterDatabase.Characteristic; } }
        public override int DataId { get { return CharacterDatabase.DataId; } set { } }
        public override int FactionId { get { return Faction == null ? 0 : Faction.DataId; } set { } }
        public float DestroyDelay { get { return destroyDelay; } }
        public float DestroyRespawnDelay { get { return destroyRespawnDelay; } }

        protected bool _isDestroyed;
        protected readonly HashSet<string> _looters = new HashSet<string>();
        protected readonly List<CharacterItem> _droppingItems = new List<CharacterItem>();
        protected float _lastTeleportToSummonerTime = 0f;
        protected int _beforeDamageReceivedHp;

        public override void PrepareRelatesData()
        {
            base.PrepareRelatesData();
            GameInstance.AddCharacters(CharacterDatabase);
        }

        public override EntityInfo GetInfo()
        {
            return new EntityInfo(EntityTypes.Monster, ObjectId, ObjectId.ToString(), DataId, FactionId, 0, 0, IsInSafeArea, Summoner);
        }

        protected override void EntityAwake()
        {
            base.EntityAwake();
            gameObject.tag = CurrentGameInstance.monsterTag;
            gameObject.layer = CurrentGameInstance.monsterLayer;
        }

        protected override void EntityUpdate()
        {
            base.EntityUpdate();
            Profiler.BeginSample("BaseMonsterCharacterEntity - Update");
            if (IsServer)
            {
                _XpTick();

                if (Time.frameCount != s_aiLastFrameCount)
                {
                    s_aiLastFrameCount = Time.frameCount;
                    s_aiTokensUsed = 0;
                    int count = Mathf.Max(1, s_aiMonsterCount);
                    s_aiTokensThisFrame = Mathf.Max(1, Mathf.CeilToInt(count * (s_aiPercentPerFrame / 100f)));
                }

                if (IsSummoned)
                {
                    if (!Summoner || Summoner.IsDead())
                    {
                        UnSummon();
                    }
                    else
                    {
                        float currentTime = Time.unscaledTime;
                        if (Vector3.Distance(EntityTransform.position, Summoner.EntityTransform.position) > CurrentGameInstance.maxFollowSummonerDistance &&
                            currentTime - _lastTeleportToSummonerTime > TELEPORT_TO_SUMMONER_DELAY)
                        {
                            Teleport(GameInstance.Singleton.GameplayRule.GetSummonPosition(Summoner), GameInstance.Singleton.GameplayRule.GetSummonRotation(Summoner), false);
                            _lastTeleportToSummonerTime = currentTime;
                        }
                    }
                }

                float now = Time.unscaledTime;

                if (now < _nextThinkAt)
                {
                    bool hasTargetNowSkip = HasValidTarget();
                    if (_hadTargetLastTick && !hasTargetNowSkip) _alertUntil = now + ALERT_LINGER_AFTER_LOS;
                    _hadTargetLastTick = hasTargetNowSkip;
                    Profiler.EndSample();
                    return;
                }

                _thinkTier = GetThinkTier();

                bool canThink = s_aiTokensUsed < s_aiTokensThisFrame;
                if (!canThink)
                {
                    bool engaged = _thinkTier == ThinkTier.Engaged;
                    if (engaged && now >= _nextEngagedBypassAt)
                    {
                        _nextEngagedBypassAt = now + 0.25f;
                        canThink = true;
                    }
                }

                if (!canThink)
                {
                    ScheduleNextThink(now);
                    bool hasTargetNowDefer = HasValidTarget();
                    if (_hadTargetLastTick && !hasTargetNowDefer) _alertUntil = now + ALERT_LINGER_AFTER_LOS;
                    _hadTargetLastTick = hasTargetNowDefer;
                    Profiler.EndSample();
                    return;
                }

                ++s_aiTokensUsed;

                // [AI decisions here: targeting, chase/stop, abilities, etc.]

                ScheduleNextThink(now);

                bool hasTargetNow = HasValidTarget();
                if (_hadTargetLastTick && !hasTargetNow) _alertUntil = now + ALERT_LINGER_AFTER_LOS;
                _hadTargetLastTick = hasTargetNow;
            }
            Profiler.EndSample();
        }

        public override void SendServerState(long writeTimestamp)
        {
            if (!IsUpdateEntityComponents) return;
            base.SendServerState(writeTimestamp);
        }

        public virtual void InitStats()
        {
            _isDestroyed = false;
            if (Level <= 0) Level = CharacterDatabase.DefaultLevel;
            ForceMakeCaches();
            CharacterStats stats = CachedData.Stats;
            CurrentHp = (int)stats.hp;
            CurrentMp = (int)stats.mp;
            CurrentStamina = (int)stats.stamina;
            CurrentFood = (int)stats.food;
            CurrentWater = (int)stats.water;
        }

        public void SetSpawnArea(GameSpawnArea<BaseMonsterCharacterEntity> spawnArea, BaseMonsterCharacterEntity spawnPrefab, int spawnLevel, Vector3 spawnPosition)
        {
            SpawnArea = spawnArea;
            SpawnPrefab = spawnPrefab;
            SpawnLevel = spawnLevel;
            SpawnPosition = spawnPosition;
        }

        protected override void SetupNetElements()
        {
            base.SetupNetElements();
            summonerObjectId.deliveryMethod = DeliveryMethod.ReliableOrdered;
            summonerObjectId.syncMode = LiteNetLibSyncField.SyncMode.ServerToClients;
            summonType.deliveryMethod = DeliveryMethod.ReliableOrdered;
            summonType.syncMode = LiteNetLibSyncField.SyncMode.ServerToClients;
        }

        public override void OnSetup()
        {
            base.OnSetup();

            if (IsClient)
            {
                if (CurrentGameInstance.monsterCharacterObjects != null && CurrentGameInstance.monsterCharacterObjects.Length > 0)
                    foreach (GameObject obj in CurrentGameInstance.monsterCharacterObjects) { if (obj != null) Instantiate(obj, EntityTransform.position, EntityTransform.rotation, EntityTransform); }
                if (CurrentGameInstance.monsterCharacterMiniMapObjects != null && CurrentGameInstance.monsterCharacterMiniMapObjects.Length > 0)
                    foreach (GameObject obj in CurrentGameInstance.monsterCharacterMiniMapObjects) { if (obj != null) Instantiate(obj, MiniMapUiTransform.position, MiniMapUiTransform.rotation, MiniMapUiTransform); }
                if (CurrentGameInstance.monsterCharacterUI != null) InstantiateUI(CurrentGameInstance.monsterCharacterUI);
            }
            if (SpawnArea == null) SpawnPosition = EntityTransform.position;
            if (IsServer) { ++s_aiMonsterCount; InitStats(); }
        }

        public void SetAttackTarget(IDamageableEntity target)
        {
            if (target.GetObjectId() == Entity.ObjectId || target.IsDead() || !target.CanReceiveDamageFrom(GetInfo())) return;
            SetTargetEntity(target.Entity);
        }

        public override float GetMoveSpeed(MovementState movementState, ExtraMovementState extraMovementState)
        {
            if (extraMovementState == ExtraMovementState.IsWalking) return CharacterDatabase.WanderMoveSpeed;
            return base.GetMoveSpeed(movementState, extraMovementState);
        }

        public override void ReceivingDamage(HitBoxPosition position, Vector3 fromPosition, EntityInfo instigator, Dictionary<DamageElement, MinMaxFloat> damageAmounts, CharacterItem weapon, BaseSkill skill, int skillLevel)
        {
            _beforeDamageReceivedHp = CurrentHp;
            base.ReceivingDamage(position, fromPosition, instigator, damageAmounts, weapon, skill, skillLevel);
        }

        public override void ReceivedDamage(HitBoxPosition position, Vector3 fromPosition, EntityInfo instigator, Dictionary<DamageElement, MinMaxFloat> damageAmounts, CombatAmountType damageAmountType, int totalDamage, CharacterItem weapon, BaseSkill skill, int skillLevel, CharacterBuff buff, bool isDamageOverTime = false)
        {
            RecordRecivingDamage(instigator, totalDamage);
            base.ReceivedDamage(position, fromPosition, instigator, damageAmounts, damageAmountType, totalDamage, weapon, skill, skillLevel, buff, isDamageOverTime);
        }

        public override void OnBuffHpDecrease(EntityInfo causer, int amount)
        {
            _beforeDamageReceivedHp = CurrentHp;
            base.OnBuffHpDecrease(causer, amount);
            RecordRecivingDamage(causer, amount);
        }

        public void RecordRecivingDamage(EntityInfo instigator, int damage)
        {
            if (instigator.TryGetEntity(out BaseCharacterEntity attackerCharacter))
            {
                if (attackerCharacter is BaseMonsterCharacterEntity m && m.IsSummonedAndSummonerExisted) attackerCharacter = m.Summoner;

                if (attackerCharacter != null)
                {
                    if (damage > _beforeDamageReceivedHp) damage = _beforeDamageReceivedHp;
                    ReceivedDamageRecord rec = new ReceivedDamageRecord();
                    rec.totalReceivedDamage = damage;
                    if (receivedDamageRecords.ContainsKey(attackerCharacter))
                    {
                        rec = receivedDamageRecords[attackerCharacter];
                        rec.totalReceivedDamage += damage;
                    }
                    rec.lastReceivedDamageTime = Time.unscaledTime;
                    receivedDamageRecords[attackerCharacter] = rec;

                    _alertUntil = Time.unscaledTime + RECENT_DAMAGE_WINDOW;
                }
            }
        }

        public override void GetAttackingData(ref bool isLeftHand, out AnimActionType animActionType, out int animationDataId, out CharacterItem weapon)
        {
            isLeftHand = false;
            animActionType = AnimActionType.AttackRightHand;
            animationDataId = 0;
            weapon = CharacterItem.Create(CurrentGameInstance.MonsterWeaponItem.DataId);
        }

        public override void GetUsingSkillData(BaseSkill skill, ref bool isLeftHand, out AnimActionType animActionType, out int animationDataId, out CharacterItem weapon)
        {
            isLeftHand = false;
            animActionType = AnimActionType.AttackRightHand;
            animationDataId = 0;
            weapon = CharacterItem.Create(CurrentGameInstance.MonsterWeaponItem.DataId);
            if (skill == null) return;
            SkillActivateAnimationType useType = CharacterModel.UseSkillActivateAnimationType(skill);
            if (useType == SkillActivateAnimationType.UseAttackAnimation && skill.IsAttack) { animationDataId = 0; animActionType = AnimActionType.AttackRightHand; }
            else if (useType == SkillActivateAnimationType.UseActivateAnimation) { animationDataId = skill.DataId; animActionType = AnimActionType.SkillRightHand; }
        }

        public override float GetAttackDistance(bool isLeftHand) { return CharacterDatabase.DamageInfo.GetDistance(); }
        public override float GetAttackFov(bool isLeftHand) { return CharacterDatabase.DamageInfo.GetFov(); }

        public override void Killed(EntityInfo lastAttacker)
        {
            base.Killed(lastAttacker);
            if (IsSummoned) return;

            Reward reward = CurrentGameplayRule.MakeMonsterReward(CharacterDatabase, Level);
            GivingRewardToKillers(FindLastAttackerPlayer(lastAttacker), reward, out float itemDropRate);
            receivedDamageRecords.Clear();
            _droppingItems.Clear();
            CharacterDatabase.RandomItems(OnRandomDropItem, itemDropRate);

            switch (CurrentGameInstance.monsterDeadDropItemMode)
            {
                case DeadDropItemMode.DropOnGround:
                    for (int i = 0; i < _droppingItems.Count; ++i)
                        ItemDropEntity.Drop(this, RewardGivenType.KillMonster, _droppingItems[i], _looters);
                    break;
                case DeadDropItemMode.CorpseLooting:
                    if (_droppingItems.Count > 0)
                        ItemsContainerEntity.DropItems(CurrentGameInstance.monsterCorpsePrefab, this, RewardGivenType.KillMonster, _droppingItems, _looters, CurrentGameInstance.monsterCorpseAppearDuration);
                    break;
            }

            if (!reward.NoExp() && CurrentGameInstance.monsterExpRewardingMode == RewardingMode.DropOnGround)
                ExpDropEntity.Drop(this, 1f, RewardGivenType.KillMonster, Level, Level, reward.exp, _looters);

            if (!reward.NoGold() && CurrentGameInstance.monsterGoldRewardingMode == RewardingMode.DropOnGround)
                GoldDropEntity.Drop(this, 1f, RewardGivenType.KillMonster, Level, Level, reward.gold, _looters);

            if (!reward.NoCurrencies() && CurrentGameInstance.monsterCurrencyRewardingMode == RewardingMode.DropOnGround)
                foreach (CurrencyAmount currencyAmount in reward.currencies)
                    if (currencyAmount.currency != null && currencyAmount.amount > 0)
                        CurrencyDropEntity.Drop(this, 1f, RewardGivenType.KillMonster, Level, Level, currencyAmount.currency, currencyAmount.amount, _looters);

            if (!IsSummoned) DestroyAndRespawn();
            _looters.Clear();
        }

        protected virtual BasePlayerCharacterEntity FindLastAttackerPlayer(EntityInfo lastAttacker)
        {
            if (!lastAttacker.TryGetEntity(out BaseCharacterEntity attackerCharacter)) return null;
            if (attackerCharacter is BaseMonsterCharacterEntity m && m.Summoner != null && m.Summoner is BasePlayerCharacterEntity)
            {
                lastAttacker = m.Summoner.GetInfo();
                lastAttacker.TryGetEntity(out attackerCharacter);
            }
            return attackerCharacter as BasePlayerCharacterEntity;
        }

        protected virtual void GivingRewardToKillers(BasePlayerCharacterEntity lastPlayer, Reward reward, out float itemDropRate)
        {
            itemDropRate = 1f;
            if (receivedDamageRecords.Count <= 0) return;

            BaseCharacterEntity tempCharacterEntity;
            bool givenRewardExp;
            bool givenRewardGold;
            bool givenRewardCurrencies;
            float tempHighRewardRate = 0f;

            foreach (BaseCharacterEntity enemy in receivedDamageRecords.Keys)
            {
                if (enemy == null) continue;

                tempCharacterEntity = enemy;
                givenRewardExp = false;
                givenRewardGold = false;
                givenRewardCurrencies = false;

                ReceivedDamageRecord rec = receivedDamageRecords[tempCharacterEntity];
                float rewardRate = (float)rec.totalReceivedDamage / (float)CachedData.MaxHp;
                if (rewardRate > 1f) rewardRate = 1f;

                if (tempCharacterEntity is BaseMonsterCharacterEntity tempMonster && tempMonster.Summoner != null && tempMonster.Summoner is BasePlayerCharacterEntity)
                    tempCharacterEntity = tempMonster.Summoner;

                if (tempCharacterEntity is BasePlayerCharacterEntity tempPlayer)
                {
                    bool isLastAttacker = lastPlayer != null && lastPlayer.ObjectId == tempPlayer.ObjectId;
                    if (isLastAttacker) tempPlayer.OnKillMonster(this);

                    if (rewardRate > tempHighRewardRate)
                    {
                        tempHighRewardRate = rewardRate;
                        _looters.Clear();
                        _looters.Add(tempPlayer.Id);
                        itemDropRate = 1f + tempPlayer.CachedData.Stats.itemDropRate;
                    }

                    GivingRewardToGuild(tempPlayer, reward, rewardRate, out float shareGuildExpRate);
                    GivingRewardToParty(tempPlayer, isLastAttacker, reward, rewardRate, shareGuildExpRate, true, out givenRewardExp, out givenRewardGold, out givenRewardCurrencies);

                    if (CurrentGameInstance.monsterExpRewardingMode == RewardingMode.Immediately && !givenRewardExp)
                    {
                        int petIndex = tempPlayer.IndexOfSummon(SummonType.PetItem);
                        if (petIndex >= 0 && tempPlayer.Summons[petIndex].CacheEntity != null)
                        {
                            BaseMonsterCharacterEntity pet = tempPlayer.Summons[petIndex].CacheEntity;
                            pet.RewardExp(reward.exp, (1f - shareGuildExpRate) * 0.5f * rewardRate, RewardGivenType.KillMonster, Level, Level);

                            int finalXpPlayer = Mathf.CeilToInt(reward.exp * (1f - shareGuildExpRate) * 0.5f * rewardRate);
                            _XpEnqueue(tempPlayer, finalXpPlayer, RewardGivenType.KillMonster, Level, Level);
                        }
                        else
                        {
                            int finalXp = Mathf.CeilToInt(reward.exp * (1f - shareGuildExpRate) * rewardRate);
                            _XpEnqueue(tempPlayer, finalXp, RewardGivenType.KillMonster, Level, Level);
                        }
                    }

                    if (CurrentGameInstance.monsterGoldRewardingMode == RewardingMode.Immediately && !givenRewardGold)
                        tempPlayer.RewardGold(reward.gold, rewardRate, RewardGivenType.KillMonster, Level, Level);

                    if (CurrentGameInstance.monsterCurrencyRewardingMode == RewardingMode.Immediately && !givenRewardCurrencies)
                        tempPlayer.RewardCurrencies(reward.currencies, rewardRate, RewardGivenType.KillMonster, Level, Level);
                }
            }
        }

        protected virtual void GivingRewardToGuild(BasePlayerCharacterEntity playerCharacterEntity, Reward reward, float rewardRate, out float shareGuildExpRate)
        {
            shareGuildExpRate = 0f;
            if (CurrentGameInstance.monsterExpRewardingMode != RewardingMode.Immediately) return;
            if (!GameInstance.ServerGuildHandlers.TryGetGuild(playerCharacterEntity.GuildId, out GuildData tempGuildData) || tempGuildData == null) return;
            shareGuildExpRate = (float)tempGuildData.ShareExpPercentage(playerCharacterEntity.Id) * 0.01f;
            if (shareGuildExpRate > 0)
                GameInstance.ServerGuildHandlers.IncreaseGuildExp(playerCharacterEntity, Mathf.CeilToInt(reward.exp * shareGuildExpRate * rewardRate));
        }
private static readonly List<BasePlayerCharacterEntity> _sharingExpMembers = new(8);
private static readonly List<BasePlayerCharacterEntity> _sharingItemMembers = new(8);

protected virtual void GivingRewardToParty(
    BasePlayerCharacterEntity playerCharacterEntity,
    bool isLastAttacker,
    Reward reward,
    float rewardRate,
    float shareGuildExpRate,
    bool makeMostDamage,
    out bool givenRewardExp,
    out bool givenRewardGold,
    out bool givenRewardCurrencies)
{
    givenRewardExp = false;
    givenRewardGold = false;
    givenRewardCurrencies = false;

    if (!GameInstance.ServerPartyHandlers.TryGetParty(playerCharacterEntity.PartyId, out PartyData tempPartyData) || tempPartyData == null)
        return;

    // ✅ reuse buffers
    _sharingExpMembers.Clear();
    _sharingItemMembers.Clear();

    BasePlayerCharacterEntity nearby;

    foreach (string memberId in tempPartyData.GetMemberIds())
    {
        if (!GameInstance.ServerUserHandlers.TryGetPlayerCharacterById(memberId, out nearby) ||
            nearby == null || nearby.IsDead())
            continue;

        if (tempPartyData.shareExp && ShouldShareExp(playerCharacterEntity, nearby))
            _sharingExpMembers.Add(nearby);

        if (tempPartyData.shareItem && ShouldShareItem(playerCharacterEntity, nearby))
            _sharingItemMembers.Add(nearby);

        if (isLastAttacker && playerCharacterEntity.ObjectId != nearby.ObjectId)
            nearby.OnKillMonster(this);
    }

    float count;

    // ---- EXP ----
    count = _sharingExpMembers.Count;
    if (count > 0 &&
        CurrentGameInstance.monsterExpRewardingMode == RewardingMode.Immediately &&
        !reward.NoExp())
    {
        for (int i = 0; i < _sharingExpMembers.Count; ++i)
        {
            nearby = _sharingExpMembers[i];
            int petIndex = nearby.IndexOfSummon(SummonType.PetItem);

            if (petIndex >= 0)
            {
                BaseMonsterCharacterEntity pet = nearby.Summons[petIndex].CacheEntity;
                if (pet != null)
                    pet.RewardExp(
                        reward.exp,
                        (1f - shareGuildExpRate) / count * 0.5f * rewardRate,
                        RewardGivenType.PartyShare,
                        playerCharacterEntity.Level,
                        Level);

                int xpPlayer = Mathf.CeilToInt(
                    reward.exp * (1f - shareGuildExpRate) / count * 0.5f * rewardRate);

                _XpEnqueue(nearby, xpPlayer, RewardGivenType.PartyShare, playerCharacterEntity.Level, Level);
            }
            else
            {
                int xp = Mathf.CeilToInt(
                    reward.exp * (1f - shareGuildExpRate) / count * rewardRate);

                var kind = (playerCharacterEntity.ObjectId == nearby.ObjectId)
                    ? RewardGivenType.KillMonster
                    : RewardGivenType.PartyShare;

                _XpEnqueue(nearby, xp, kind, playerCharacterEntity.Level, Level);
            }
        }
    }

    // ---- ITEMS / GOLD / CURRENCY ----
    count = _sharingItemMembers.Count;
    if (count > 0 &&
        ((CurrentGameInstance.monsterGoldRewardingMode == RewardingMode.Immediately && !reward.NoGold()) ||
         (CurrentGameInstance.monsterCurrencyRewardingMode == RewardingMode.Immediately && reward.NoCurrencies())))
    {
        for (int i = 0; i < _sharingItemMembers.Count; ++i)
        {
            nearby = _sharingItemMembers[i];

            if (makeMostDamage)
                _looters.Add(nearby.Id);

            float mul = 1f / count * rewardRate;
            var kind = playerCharacterEntity.ObjectId == nearby.ObjectId
                ? RewardGivenType.KillMonster
                : RewardGivenType.PartyShare;

            if (CurrentGameInstance.monsterGoldRewardingMode == RewardingMode.Immediately)
                nearby.RewardGold(reward.gold, mul, kind, Level, Level);

            if (CurrentGameInstance.monsterCurrencyRewardingMode == RewardingMode.Immediately)
                nearby.RewardCurrencies(reward.currencies, mul, kind, Level, Level);
        }
    }

    if (tempPartyData.shareExp)
        givenRewardExp = true;

    if (tempPartyData.shareItem)
    {
        givenRewardGold = true;
        givenRewardCurrencies = true;
    }
}

        private bool ShouldShareExp(BasePlayerCharacterEntity attacker, BasePlayerCharacterEntity member)
        {
            return GameInstance.Singleton.partyShareExpDistance <= 0f || Vector3.Distance(attacker.EntityTransform.position, member.EntityTransform.position) <= GameInstance.Singleton.partyShareExpDistance;
        }

        private bool ShouldShareItem(BasePlayerCharacterEntity attacker, BasePlayerCharacterEntity member)
        {
            return GameInstance.Singleton.partyShareItemDistance <= 0f || Vector3.Distance(attacker.EntityTransform.position, member.EntityTransform.position) <= GameInstance.Singleton.partyShareItemDistance;
        }

        private void OnRandomDropItem(BaseItem item, int amount)
        {
            int maxStack = item.MaxStack;
            while (amount > 0)
            {
                int stackSize = Mathf.Min(maxStack, amount);
                _droppingItems.Add(CharacterItem.Create(item, 1, stackSize));
                amount -= stackSize;
            }
        }

        public virtual void DestroyAndRespawn()
        {
            if (!IsServer) return;
            CurrentHp = 0;
            if (_isDestroyed) return;
            _isDestroyed = true;
            --s_aiMonsterCount;
            NetworkDestroy(DestroyDelay);
            if (SpawnArea != null) SpawnArea.Spawn(SpawnPrefab, SpawnLevel, DestroyDelay + DestroyRespawnDelay);
            else if (Identity.IsSceneObject) RespawnRoutine(DestroyDelay + DestroyRespawnDelay).Forget();
        }

        private async UniTaskVoid RespawnRoutine(float delay)
        {
            await UniTask.Delay(Mathf.CeilToInt(delay * 1000));
            Teleport(SpawnPosition, EntityTransform.rotation, false);
            InitStats();
            Manager.Assets.NetworkSpawnScene(Identity.ObjectId, SpawnPosition, CurrentGameInstance.DimensionType == DimensionType.Dimension3D ? Quaternion.Euler(Vector3.up * Random.Range(0, 360)) : Quaternion.identity);
            OnRespawn();
        }

        public void Summon(BaseCharacterEntity summoner, SummonType summonType, int level)
        {
            Summoner = summoner;
            SummonType = summonType;
            Level = level;
            InitStats();
        }

        public void UnSummon()
        {
            if (IsServer) --s_aiMonsterCount;
            NetworkDestroy();
        }

        // ---------- Helpers ----------
        private ThinkTier GetThinkTier()
        {
            if (HasValidTarget()) return ThinkTier.Engaged;
            float now = Time.unscaledTime;
            foreach (var kv in receivedDamageRecords)
                if (now - kv.Value.lastReceivedDamageTime <= RECENT_DAMAGE_WINDOW)
                    return ThinkTier.Alert;
            if (now <= _alertUntil) return ThinkTier.Alert;
            return ThinkTier.Idle;
        }

        private void ScheduleNextThink(float now)
        {
            if (_thinkJitter <= 0f) _thinkJitter = Random.Range(0f, 0.08f);
            float period = _thinkTier == ThinkTier.Engaged ? 0.11f : _thinkTier == ThinkTier.Alert ? 0.18f : 0.80f;
            _nextThinkAt = now + period + _thinkJitter;
        }

        private bool HasValidTarget()
        {
            var tgt = GetTargetEntity();
            if (tgt == null) return false;
            if (tgt is BaseCharacterEntity bce) return !bce.IsDead();
            if (tgt is IDamageableEntity dmg) return !dmg.IsDead();
            return false;
        }
    }

    public struct ReceivedDamageRecord
    {
        public float lastReceivedDamageTime;
        public int totalReceivedDamage;
    }
}
