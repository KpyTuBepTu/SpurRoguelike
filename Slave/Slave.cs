using System;
using System.Collections.Generic;
using System.Linq;
using SpurRoguelike.Core;
using SpurRoguelike.Core.Primitives;
using SpurRoguelike.Core.Views;

namespace Slave
{
    public class Bot : IPlayerController
    {
        private int lvlCount = 0;
        private Slave slave;

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {
            if (levelView.Player.Location == levelView.Field.GetCellsOfType(CellType.PlayerStart).Single())
            {
                lvlCount++;
                slave = new Slave(ref levelView, messageReporter, lvlCount);
            }

            return slave.SlaveObjectives.Peek()();
        }
    }

    /// <summary>
    /// Основные действия выполняемые персонажем
    /// </summary>
    public enum SlaveActions : int
    {
        GoToExit,
        FindBestEquip,
        MakeYourselfStronger,
        GoHealYourself,
    }

    public class Slave
    {
        public Stack<Func<Turn>> SlaveObjectives { get; private set; }

        private List<Func<Turn>> objectives;       
        private LevelView levelView;
        private Map map;
        private IMessageReporter messageReporter;
        private int lvl;
        private int panicHealthValue;
        private bool isFinalRoom;

        public Slave(ref LevelView level, IMessageReporter messageReporter, int lvl)
        {
            SlaveObjectives = new Stack<Func<Turn>>();
            map = new Map(ref level);
            this.messageReporter = messageReporter;
            levelView = level;
            this.lvl = lvl;
            panicHealthValue = 15 * lvl;
            isFinalRoom = level.Monsters.Count() == 1 ? true : false;

            objectives = new List<Func<Turn>>();
            objectives.Add(GoToExit);
            objectives.Add(FindBestEquip);
            objectives.Add(MakeYourselfStronger);
            objectives.Add(GoHealYourself);

            NewLvlObjectives();
        }

        private void NewLvlObjectives()
        {
            SlaveObjectives.Push(objectives[(int)SlaveActions.GoToExit]);
        }

        public Turn GoToExit()
        {
            messageReporter.ReportMessage("-----GoToExit-----");
            
            // "Анализ" ситуации. Для каждого из основных действий, кроме хила
            Turn turn = CheckEquipAndExp();
            if (turn != Turn.None)
                return turn;
            turn = SearchMonstersAround(); 
            if (turn != Turn.None)
                return turn;

            if (GetClosestFruit(levelView.HealthPacks).HasValue)
            {
                SlaveObjectives.Push(objectives[(int)SlaveActions.GoHealYourself]);
                return SlaveObjectives.Peek()();
            }

            return CreateMap(levelView.Field.GetCellsOfType(CellType.Exit).Single(), false);
        }

        private Turn FindBestEquip()
        {
            messageReporter.ReportMessage("-----FindBestEquip-----");
            Turn turn = SearchMonstersAround();
            if (turn != Turn.None)
                return turn;
            turn = CheckEquipAndExp();
            if (turn != Turn.None)
                return turn;

            // подъем первой попавшейся шмотки в начале игры
            if (levelView.Player.TotalAttack == levelView.Player.Attack && levelView.Player.TotalDefence == levelView.Player.Defence)
            {
                var closestItem = levelView.Items
                    .Select(i => i.Location)
                    .OrderBy(i => Math.Sqrt(Math.Pow(levelView.Player.Location.X - i.X, 2) + Math.Pow(levelView.Player.Location.Y - i.Y, 2)))
                    .First();

                return CreateMap(closestItem, false);
            }
            else
            {
                var bestItem = CheckEquipOnField();

                if (bestItem.HasValue)
                    return CreateMap(bestItem.Location, false);
                else
                    return ObjectiveDoneOrImpossible();
            }
        }

        private ItemView CheckEquipOnField()
        {
            var bestBonuses = levelView.Items
                    .Select(i => i.AttackBonus + i.DefenceBonus)
                    .OrderByDescending(i => i)
                    .First();

            return levelView.Items
                .Where(i => i.AttackBonus + i.DefenceBonus == bestBonuses && i.AttackBonus + i.DefenceBonus > levelView.Player.TotalAttack + levelView.Player.TotalDefence - levelView.Player.Attack - levelView.Player.Defence)
                .OrderBy(i => Math.Sqrt(Math.Pow(levelView.Player.Location.X - i.Location.X, 2) + Math.Pow(levelView.Player.Location.Y - i.Location.Y, 2)))
                .FirstOrDefault();
        }

        private Turn MakeYourselfStronger()
        {
            messageReporter.ReportMessage("-----MakeYourselfStronger-----");
            var closestMonster = CheckOnAvailableExperience();

            Turn turn;
            if (!isFinalRoom)
            {
                turn = SearchMonstersAround();
                if (turn != Turn.None)
                    return turn;
            }
            turn = CheckEquipAndExp();
            if (turn != Turn.None)
                return turn;

            if (closestMonster != default(MonsterFightInfo))
                if (levelView.Player.Location.IsInRange(closestMonster.Info.Location, 1))
                    // на финального босса
                    if (levelView.Player.Health > 30)
                        return Turn.Attack(closestMonster.Info.Location - levelView.Player.Location);
                    else
                        return Escape();
                else
                    return CreateMap(closestMonster.Info.Location, false);
            else
                return ObjectiveDoneOrImpossible();
        }

        private MonsterFightInfo CheckOnAvailableExperience()
        {
            // цифра - кол-во ходов до моба, регулировка обзора персонажа
            int visionToFight = levelView.Player.Location.X + levelView.Player.Location.Y - 100;
            return levelView.Monsters
                    .Select(m => new MonsterFightInfo(m, levelView.Player))
                    .Where(m => visionToFight <= m.Info.Location.X + m.Info.Location.Y)
                    .OrderBy(m => Math.Sqrt(Math.Pow(levelView.Player.Location.X - m.Info.Location.X, 2) + Math.Pow(levelView.Player.Location.Y - m.Info.Location.Y, 2)))
                    .FirstOrDefault();
        }

        private Turn GoHealYourself()
        {
            messageReporter.ReportMessage("-----GoHealYourself-----");
            // ежеходное обновление карты, чтобы быть совершенно точно уверенным в корректности данных, при хиле риск минимизируется
            map = new Map(ref levelView);
            var fruits = levelView.HealthPacks.ToList();
            var closestFruit = GetClosestFruit(fruits);

            if (closestFruit.HasValue)
            {
                // перебор хилок, если ближайшая недоступна
                Turn turn = CreateMap(closestFruit.Location, true);
                while (turn == Turn.None || fruits.Count == 0)
                {
                    fruits.Remove(closestFruit);
                    turn = CreateMap(closestFruit.Location, true);
                }
                if (turn == Turn.None || fruits.Count == 0)
                {
                    var closestMonster = levelView.Monsters.Where(m => m.Location.IsInRange(levelView.Player.Location, 1)).FirstOrDefault();
                    if (closestMonster.HasValue)
                        return Turn.Attack(closestMonster.Location - levelView.Player.Location);
                    else
                        return CreateMap(levelView.Field.GetCellsOfType(CellType.Exit).Single(), false);
                }
                else
                    return turn;
            }
            else
                return ObjectiveDoneOrImpossible();
        }

        private HealthPackView GetClosestFruit(IEnumerable<HealthPackView> fruits)
        {
            if (isFinalRoom)
            {
                if (levelView.Player.Health <= 30)
                    return fruits
                        .OrderBy(i => Math.Sqrt(Math.Pow(levelView.Player.Location.X - i.Location.X, 2) + Math.Pow(levelView.Player.Location.Y - i.Location.Y, 2)))
                        .FirstOrDefault();
                else
                    return default(HealthPackView);
            }
            else
            {
                if (levelView.Player.Health < 100)
                    return fruits
                        .OrderBy(i => Math.Sqrt(Math.Pow(levelView.Player.Location.X - i.Location.X, 2) + Math.Pow(levelView.Player.Location.Y - i.Location.Y, 2)))
                        .FirstOrDefault();
                else
                    return default(HealthPackView);
            }
        }

        private Turn CreateMap(Location location, bool moreSafety)
        {
            Turn turn = CheckAllMapVariants(location, moreSafety);
            if (turn != Turn.None)
                return turn;
            else
                return ObjectiveDoneOrImpossible();
        }

        private Turn CheckAllMapVariants(Location location, bool moreSafety)
        {
            if (!map.IsContainsCell(location))
                map.AddLocationToCells(location);
            map.SearchPath(location, moreSafety);
            if (map.PathExist)
                return map.GetNextStep();
            else
            {
                map.AddFruitsToMap();
                map.SearchPath(location, moreSafety);
                if (map.PathExist)
                    return map.GetNextStep();
                else
                {
                    map.AddItemsToMap();
                    map.SearchPath(location, moreSafety);
                    if (map.PathExist)
                        return map.GetNextStep();
                    else
                        return Turn.None;
                    {
                        map.AddTrapsToMap();
                        map.SearchPath(location, moreSafety);
                        if (map.PathExist)
                            return map.GetNextStep();
                        else
                            return Turn.None;
                    }
                }
            }
        }

        private Turn ObjectiveDoneOrImpossible()
        {
            SlaveObjectives.Pop();
            // обновление карты, после выполнения цели
            map = new Map(ref levelView);
            messageReporter.ReportMessage("-----ObjectiveDoneOrImpossible-----");
            return SlaveObjectives.Peek()();
        }

        private Turn CheckEquipAndExp()
        {
            if (CheckEquipOnField().HasValue && SlaveObjectives.Peek() != objectives[(int)SlaveActions.FindBestEquip])
            {
                if (SlaveObjectives.Peek() == objectives[(int)SlaveActions.GoHealYourself])
                    return Turn.None;

                SlaveObjectives.Push(FindBestEquip);
                return SlaveObjectives.Peek()();
            }
            else if (CheckOnAvailableExperience() != default(MonsterFightInfo) && SlaveObjectives.Peek() != objectives[(int)SlaveActions.MakeYourselfStronger] && SlaveObjectives.Peek() != objectives[(int)SlaveActions.FindBestEquip])
            {
                if (SlaveObjectives.Peek() == objectives[(int)SlaveActions.GoHealYourself])
                    return Turn.None;

                SlaveObjectives.Push(MakeYourselfStronger);
                return SlaveObjectives.Peek()();
            }
            else
                return Turn.None;
        }

        private Turn SearchMonstersAround()
        {
            if (panicHealthValue > 50)
                panicHealthValue = 50;

            var nearbyMonsters = levelView.Monsters
                .Where(m => m.Location.IsInRange(levelView.Player.Location, 1))
                .Select(m => new MonsterFightInfo(m, levelView.Player));

            if (nearbyMonsters.Count() == 0)
                return Turn.None;
            else if (nearbyMonsters.Count() == 1)
            {
                var monster = nearbyMonsters.Single();
                if (monster.IsSaveToKill && levelView.Player.Health > panicHealthValue)
                {
                    messageReporter.ReportMessage("-----Monster Health: " + monster.Info.Health);
                    return Turn.Attack(monster.Info.Location - levelView.Player.Location);
                }
                else
                    return Escape();
            }
            else if (nearbyMonsters.Count() == 2)
            {
                var totalDamage = nearbyMonsters.Sum(m => m.DamageToSlave);
                var saveToKill = nearbyMonsters
                    .OrderBy(m => m.HitsToDeath)
                    .Where(m => (m.HitsToDeath + 4 < levelView.Player.Health / totalDamage) || (levelView.Player.Health >= panicHealthValue * lvl && levelView.Player.Health > totalDamage))
                    .FirstOrDefault();

                if (saveToKill != default(MonsterFightInfo))
                    return Turn.Attack(saveToKill.Info.Location - levelView.Player.Location);
                else
                    return Escape();
            }
            else
                return Escape();
        }

        private Turn Escape()
        {
            SlaveObjectives.Push(GoHealYourself);
            return SlaveObjectives.Peek()();
        }     
    }

    /// <summary>
    /// Поиск пути до объекта.
    /// Сейчас, при каждом ходе, строится новый путь.
    /// Сделано так, из-за "большого" количества движущихся объектов и неперемещения персонажа на новую клетку при подборе шмотки или хилки
    /// Первоначально, по плану, путь перестраивался с каждой новой целью.
    /// Но где-то был косяк и, вследствие недостаточности времени, решил не тыкаться дальше, в попытках его исправить, а сделать хотябы так
    /// </summary>
    public class Map
    {
        private LevelView level;
        private List<Location> cells;
        private Stack<Location> path;
        
        public bool PathExist { get; private set; }

        public Map(ref LevelView level)
        {
            path = new Stack<Location>();
            this.level = level;
            InitializeMap();
        }

        private void InitializeMap()
        {
            path = new Stack<Location>();
            cells = level.Field.GetCellsOfType(CellType.Empty)
                .Where(l => !level.Items.Select(i => i.Location).Contains(l) && !level.HealthPacks.Select(i => i.Location).Contains(l) && !level.Monsters.Select(i => i.Location).Contains(l))
                .ToList();
            cells.Add(level.Field.GetCellsOfType(CellType.Exit).Single());
            cells.Add(level.Field.GetCellsOfType(CellType.PlayerStart).Single());
        }

        public void SearchPath(Location finish, bool moreSafety)
        {
            path = new Stack<Location>();
            Dictionary<Location, int> waves = new Dictionary<Location, int>();
            waves.Add(level.Player.Location, 0);

            int previousCount = 0;
            bool isNoReply = true;
            PathExist = false;
            while (isNoReply)
            {
                int currentCount = waves.Count;
                for (var i = previousCount; i < currentCount; i++)
                {
                    int wave = waves[waves.Keys.ElementAt(i)];
                    List<Location> stepPosition = GetStepsAround(waves.Keys.ElementAt(i)); 
                    foreach (var pos in stepPosition)
                        if (cells.Contains(pos) && !waves.Keys.Contains(pos))
                        {
                            waves.Add(pos, wave + 1);
                            if (pos.Equals(finish))
                                PathExist = true;
                        }
                }
                previousCount = currentCount;
                // добавив в условие PathExist увеличится скорость нахожения, но уменьшится варитативность 
                if (previousCount == waves.Count)
                    isNoReply = false;
            }
            if (waves.Keys.Contains(finish))
            {
                path.Push(finish);
                Location position = path.Peek();
                List<Location> history = new List<Location>();
                history.Add(position);

                while (true)
                {
                    List<Location> stepPosition;
                    if (moreSafety)
                        stepPosition = GetStepsAround(position)
                            .OrderBy(i => level.Monsters.Where(m => m.Location.IsInRange(i, 1)).Count())
                            .ToList();
                    else
                        stepPosition = GetStepsAround(position)
                            .OrderBy(i => Math.Sqrt(Math.Pow(level.Player.Location.X - i.X, 2) + Math.Pow(level.Player.Location.Y - i.Y, 2)))
                            .ToList();
                    bool positionAdded = false;
                    foreach (var pos in stepPosition)
                        if (!history.Contains(pos) && waves.Keys.Contains(pos) && waves[pos] == waves[position] - 1)
                        {
                            position = pos;
                            positionAdded = true;
                            history.Add(position);
                            break;
                        }
                    if (position == level.Player.Location)
                        break;
                    else if (positionAdded)
                        path.Push(position);
                    else
                    {
                        path.Pop();
                        position = path.Peek();
                    }
                }
            }
        }

        private List<Location> GetStepsAround(Location location)
        {
            List<Location> stepPosition = new List<Location>();
            stepPosition.Add(new Location(location.X, location.Y - 1));
            stepPosition.Add(new Location(location.X, location.Y + 1));
            stepPosition.Add(new Location(location.X - 1, location.Y));
            stepPosition.Add(new Location(location.X + 1, location.Y));
            return stepPosition;
        }

        public void AddLocationToCells(Location location)
        {
            cells.Add(location);
        }

        public void DeleteLocationFromCells(Location location)
        {
            cells.Remove(location);
        }

        public void AddItemsToMap()
        {
            InitializeMap();
            cells.AddRange(level.Items.Select(i => i.Location));
        }

        public void AddFruitsToMap()
        {
            InitializeMap();
            cells.AddRange(level.HealthPacks.Select(i => i.Location));
        }

        public void AddTrapsToMap()
        {
            InitializeMap();
            cells.AddRange(level.Field.GetCellsOfType(CellType.Trap));
        }

        public Turn GetNextStep()
        {
            if (path.Count > 0)
                return Turn.Step(path.Pop() - level.Player.Location);
            else
                return Turn.None;
        }

        public bool IsContainsCell(Location location)
        {
            return cells.Contains(location);
        }
    }

    public class MonsterFightInfo
    {
        public PawnView Info { get; private set; }
        public int DamageToSlave { get; private set; }
        public int DamageFromSlave { get; private set; }
        public bool IsSaveToKill { get; private set; }
        public int HitsToKillSlave { get; private set; }
        public int HitsToDeath { get; set; }

        public MonsterFightInfo(PawnView monster, PawnView slave)
        {
            Info = monster;
            // худший вариант для раба, лучший для моба
            DamageToSlave = monster.TotalAttack * monster.Attack / slave.TotalDefence;
            DamageFromSlave = (int)(slave.TotalAttack * slave.Attack * 0.9 / monster.TotalDefence);
            HitsToKillSlave = slave.Health / DamageToSlave;
            HitsToDeath = monster.Health / DamageFromSlave;
            IsSaveToKill = HitsToKillSlave > HitsToDeath - 1;
        }
    }
}
