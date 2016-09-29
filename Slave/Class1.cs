using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpurRoguelike.Core;
using SpurRoguelike.Core.Primitives;
using SpurRoguelike.Core.Views;

namespace Slave
{
    public class Bot : IPlayerController
    {
        private Slave slave;

        public Turn MakeTurn(LevelView levelView, IMessageReporter messageReporter)
        {
            if (levelView.Player.Location == levelView.Field.GetCellsOfType(CellType.PlayerStart).Single())
            {
                slave = new Slave(ref levelView, messageReporter);
            }

            return Turn.None;
        }
    }

    public enum SlaveActions : int
    {
        GoToExit,
        FindBestEquip,
        MakeYourselfStronger,
        GoHealYourself,
        StartJourney
    }

    public class Slave
    {
        private List<Func<Turn>> Objectives { get; set; }
        private Stack<Func<Turn>> SlaveObjectives { get; set; }
        private LevelView level;
        private Map map;
        private IMessageReporter messageReporter;
        private bool isNewObjective;

        public Slave(ref LevelView level, IMessageReporter messageReporter)
        {
            SlaveObjectives = new Stack<Func<Turn>>();
            this.messageReporter = messageReporter;
            this.level = level;

            Objectives = new List<Func<Turn>>();
            Objectives.Add(GoToExit);
            Objectives.Add(FindBestEquip);
            Objectives.Add(MakeYourselfStronger);
            Objectives.Add(GoHealYourself);
            Objectives.Add(StartJourney);

            NewLvlObjectives();
        }

        private void NewLvlObjectives()
        {
            SlaveObjectives.Push(Objectives[(int)SlaveActions.GoToExit]);
            SlaveObjectives.Push(Objectives[(int)SlaveActions.FindBestEquip]);
            SlaveObjectives.Push(Objectives[(int)SlaveActions.StartJourney]);
            isNewObjective = true;
        }

        private Turn StartJourney()
        {
            return ObjectiveDoneOrImpossible();
        }

        private Turn GoToExit()
        {
            if (!isNewObjective)
                return map.GetNextStep();
            else
            {
                isNewObjective = false;
                return CreateMap(level.Field.GetCellsOfType(CellType.Exit).Single());
            }
        }

        private Turn FindBestEquip()
        {
            if (!isNewObjective)
                return map.GetNextStep();
            else
            {
                isNewObjective = false;
                if (level.Player.TotalAttack == level.Player.Attack && level.Player.TotalDefence == level.Player.Defence)
                {
                    var closestItem = level.Items
                        .Select(i => i.Location)
                        .OrderBy(i => Math.Sqrt(Math.Pow(level.Player.Location.X - i.X, 2) + Math.Pow(level.Player.Location.Y - i.Y, 2)))
                        .First();

                    return CreateMap(closestItem);
                }
                else
                {
                    var bestItem = CheckEquipOnField();

                    if (bestItem.HasValue)
                        return CreateMap(bestItem.Location);
                    else
                        return ObjectiveDoneOrImpossible();
                }
            }
        }

        private ItemView CheckEquipOnField()
        {
            var bestBonuses = level.Items
                    .Select(i => i.AttackBonus + i.DefenceBonus)
                    .OrderByDescending(i => i)
                    .First();

            return level.Items
                .Where(i => i.AttackBonus + i.DefenceBonus == bestBonuses && i.AttackBonus + i.DefenceBonus > level.Player.TotalAttack + level.Player.TotalDefence - level.Player.Attack - level.Player.Defence)
                .OrderBy(i => Math.Sqrt(Math.Pow(level.Player.Location.X - i.Location.X, 2) + Math.Pow(level.Player.Location.Y - i.Location.Y, 2)))
                .FirstOrDefault();
        }

        private Turn MakeYourselfStronger()
        {
            //if (!isNewObjective)
            //    return map.GetNextStep();
            //else
            //{
            //    isNewObjective = false;
                var closestMonster = CheckOnAvailableExperience();

                if (closestMonster != default(MonsterFightInfo))
                    if (level.Player.Location.IsInRange(closestMonster.Info.Location, 1))
                        return Turn.Attack(closestMonster.Info.Location - level.Player.Location);
                    else
                        return CreateMap(closestMonster.Info.Location);
                else
                    return ObjectiveDoneOrImpossible();
            //}
        }

        private MonsterFightInfo CheckOnAvailableExperience()
        {
            // цифра - кол-во ходов до моба
            int visionToFight = level.Player.Location.X + level.Player.Location.Y - 25;
            return level.Monsters
                    .Select(m => new MonsterFightInfo(m, level.Player))
                    .Where(m => m.IsSaveToKill &&
                                (m.Info.Attack * 1.5 >= level.Player.Attack || m.Info.Defence * 1.5 >= level.Player.Defence)
                                && visionToFight <= m.Info.Location.X + m.Info.Location.Y)
                    .OrderBy(m => Math.Sqrt(Math.Pow(level.Player.Location.X - m.Info.Location.X, 2) + Math.Pow(level.Player.Location.Y - m.Info.Location.Y, 2)))
                    .FirstOrDefault();
        }

        private Turn GoHealYourself()
        {
            if (!isNewObjective)
                return map.GetNextStep();
            else
            {
                isNewObjective = false;
                var closestFruit = GetClosestFruit();

                if (closestFruit.HasValue)
                    return CreateMap(closestFruit.Location);
                else
                    return ObjectiveDoneOrImpossible();
            }
        }

        private HealthPackView GetClosestFruit()
        {
            return level.HealthPacks
                .OrderBy(i => Math.Sqrt(Math.Pow(level.Player.Location.X - i.Location.X, 2) + Math.Pow(level.Player.Location.Y - i.Location.Y, 2)))
                .FirstOrDefault();
        }

        private Turn CreateMap(Location location)
        {
            Turn turn = CheckAllMapVariants(location);
            if (turn != Turn.None)
                return turn;
            else
                return ObjectiveDoneOrImpossible();
        }

        private Turn CheckAllMapVariants(Location location)
        {
            if (!map.IsContainsCell(location))
                map.AddLocationToCells(location);
            map.SearchPath(location);
            if (map.PathExist)
                return map.GetNextStep();
            else
            {
                map.AddFruitsToMap();
                map.SearchPath(location);
                if (map.PathExist)
                    return map.GetNextStep();
                else
                {
                    map.AddItemsToMap();
                    map.SearchPath(location);
                    if (map.PathExist)
                        return map.GetNextStep();
                    else
                    {
                        map.AddTrapsToMap();
                        map.SearchPath(location);
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
            isNewObjective = true;
            SlaveObjectives.Pop();
            map = new Map(ref level);
            return SlaveObjectives.Peek()();
        }
    }

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
            cells = level.Field.GetCellsOfType(CellType.Empty)
                .Where(l => !level.Items.Select(i => i.Location).Contains(l) && !level.HealthPacks.Select(i => i.Location).Contains(l))
                .ToList();
            cells.Add(level.Field.GetCellsOfType(CellType.Exit).Single());
        }

        public void SearchPath(Location finish)
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
                if ((previousCount == waves.Count) || PathExist)
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
                    List<Location> stepPosition = GetStepsAround(position);
                    stepPosition = stepPosition.OrderBy(i => Math.Sqrt(Math.Pow(level.Player.Location.X - i.X, 2) + Math.Pow(level.Player.Location.Y - i.Y, 2))).ToList();
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

        public void RefreshMap()
        {
            InitializeMap();
        }

        public void AddLocationToCells(Location location)
        {
            cells.Add(location);
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
