using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PRUNner.Backend;
using PRUNner.Backend.Data;
using PRUNner.Backend.Data.Components;
using PRUNner.Backend.Data.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace PRUNner.Models.BasePlanner
{
    public class PlanetBuilding : ReactiveObject
    {
        public BuildingData Building { get; }
        
        [Reactive] public int Amount { get; set; }
        
        public bool IsProductionBuilding => Building.Category != BuildingCategory.Infrastructure;
        public List<PlanetBuildingProductionElement> AvailableRecipes { get; }
        
        public ObservableCollection<PlanetBuildingProductionQueueElement> Production { get; } = new();
        
        public PlanetBuilding() // This feels like a hack, but otherwise we can't set the Design.DataContext in BuildingRow...
        {
            Building = null!;
        }

        public static PlanetBuilding FromInfrastructureBuilding(BuildingData building)
        {
            return new(building);
        }
        
        public static PlanetBuilding FromProductionBuilding(PlanetData planet, BuildingData building)
        {
            return new(planet, building);
        }
        
        private PlanetBuilding(BuildingData building)
        {
            Building = building;
            AvailableRecipes = null!;
        }
        
        private PlanetBuilding(PlanetData planet, BuildingData building)
        {
            Building = building;
            
            if (Building.Category == BuildingCategory.Resources)
            {
                AvailableRecipes = GetResourceRecipeList(planet, building);
            }
            else
            {
                AvailableRecipes = Building.Production.Select(x =>  new PlanetBuildingProductionElement(x)).ToList();
            }
            
            AddProduction();
        }

        private List<PlanetBuildingProductionElement> GetResourceRecipeList(PlanetData planetData, BuildingData building)
        {
            IEnumerable<ResourceData> resources;
            switch (building.Ticker)
            {
                case Names.Buildings.COL:
                    resources = planetData.Resources.Where(x => x.ResourceType == ResourceType.Gaseous);
                    break;
                case Names.Buildings.EXT:
                    resources = planetData.Resources.Where(x => x.ResourceType == ResourceType.Mineral);
                    break;
                case Names.Buildings.RIG:
                    resources = planetData.Resources.Where(x => x.ResourceType == ResourceType.Liquid);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(building.Ticker), building.Ticker, null);
            }

             return resources.Select(x => new PlanetBuildingProductionElement(x)).ToList();
        }

        public int CalculateNeededArea()
        {
            return Building.AreaCost * Amount;
        }
        
        public void AddProduction()
        {
            var production = new PlanetBuildingProductionQueueElement(this);
            Production.Add(production);
        }

        public void RemoveProduction(PlanetBuildingProductionQueueElement element)
        {
            Production.Remove(element);
        }
    }
}