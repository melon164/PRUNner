using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DynamicData.Binding;
using PRUNner.Backend.Data;
using PRUNner.Backend.Data.Components;
using PRUNner.Backend.Data.Enums;
using PRUNner.Backend.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace PRUNner.Backend.BasePlanner
{
    public class PlanetBuilding : ReactiveObject
    {
        public BuildingData Building { get; }
        public PlanetaryBase PlanetaryBase { get; }

        [Reactive] public int Amount { get; set; }

        public AdvancedBuildingConfiguration AdvancedBuildingConfiguration { get; } = new();
        private readonly double _fertilityBonus;

        public ObservableCollection<PlanetBuildingProductionRecipe>? AvailableRecipes { get; }
        
        public ObservableCollection<PlanetBuildingProductionQueueElement> Production { get; } = new();
        public ImmutableArray<MaterialIO> BuildingMaterials { get; init; } 
        
        public double Efficiency { get; private set; }
        public double BuildingCost { get; private set; }
        public double DailyCostForRepairs { get; private set; }

        public PlanetBuilding()
        {
            PlanetaryBase = null!;
            Building = null!;
            BuildingMaterials = ImmutableArray<MaterialIO>.Empty;
            _fertilityBonus = 0;

            AdvancedBuildingConfiguration.Changed.Subscribe(_ => this.RaisePropertyChanged(nameof(AdvancedBuildingConfiguration)));
        }

        private PlanetBuilding(PlanetaryBase planetaryBase, BuildingData building) : this()
        {
            PlanetaryBase = planetaryBase;
            Building = building;

            BuildingMaterials = building.GetBuildingMaterialsOnPlanet(planetaryBase.Planet);
        }
        
        private PlanetBuilding(PlanetaryBase planetaryBase, PlanetData planet, BuildingData building) : this(planetaryBase, building)
        {
            _fertilityBonus = building.AffectedByFertility ? 1 + planet.Fertility * 0.3 : 1;
            
            if (Building.Category == BuildingCategory.Resources)
            {
                AvailableRecipes = GetResourceRecipeList(planet, building);
            }
            else
            {
                AvailableRecipes = new ObservableCollection<PlanetBuildingProductionRecipe>(Building.Production.Select(x =>  new PlanetBuildingProductionRecipe(this, x)));
            }
            
            AddProduction();

            this.WhenPropertyChanged(x => x.Amount).Subscribe(value => RecalculateBuildingCosts());
            RecalculateBuildingCosts();
        }
        
        public static PlanetBuilding FromInfrastructureBuilding(PlanetaryBase planetaryBase, BuildingData building)
        {
            return new(planetaryBase, building);
        }
        
        public static PlanetBuilding FromProductionBuilding(PlanetaryBase planetaryBase, PlanetData planet, BuildingData building)
        {
            return new(planetaryBase, planet, building);
        }

        private void RecalculateBuildingCosts()
        {
            var totalCost = 0d;
            foreach (var material in BuildingMaterials)
            {
                totalCost += material.Material.PriceData.GetPrice(true, PlanetaryBase.Empire) * material.Amount;
            }
            
            BuildingCost = totalCost;
            DailyCostForRepairs = totalCost / Constants.DaysUntilAllBuildingMaterialsAreLost;
        }

        private ObservableCollection<PlanetBuildingProductionRecipe> GetResourceRecipeList(PlanetData planet, BuildingData building)
        {
            IEnumerable<ResourceData> resources;
            switch (building.Ticker)
            {
                case Names.Buildings.COL:
                    resources = planet.Resources.Where(x => x.ResourceType == ResourceType.Gaseous);
                    break;
                case Names.Buildings.EXT:
                    resources = planet.Resources.Where(x => x.ResourceType == ResourceType.Mineral);
                    break;
                case Names.Buildings.RIG:
                    resources = planet.Resources.Where(x => x.ResourceType == ResourceType.Liquid);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(building.Ticker), building.Ticker, null);
            }

             return new ObservableCollection<PlanetBuildingProductionRecipe>(resources.Select(x => new PlanetBuildingProductionRecipe(this, x)));
        }

        public int CalculateNeededArea()
        {
            return Building.AreaCost * Amount;
        }

        public delegate void OnProductionUpdateEvent();
        public event OnProductionUpdateEvent? OnProductionUpdate;

        public PlanetBuildingProductionQueueElement AddProduction()
        {
            var production = new PlanetBuildingProductionQueueElement(this);
            Production.Add(production);
            production.Changed.Subscribe(_ => OnProductionUpdate?.Invoke());
            production.Changed.Subscribe(_ => UpdateProductionPercentages());
            production.Changed.Subscribe(_ => production.UpdateDuration());
            UpdateProductionPercentages();
            production.UpdateDuration();
            return production;
        }

        public void RemoveProduction(PlanetBuildingProductionQueueElement element)
        {
            Production.Remove(element);
            OnProductionUpdate?.Invoke();
            UpdateProductionPercentages();
        }

        public void UpdateProductionEfficiency(WorkforceSatisfaction workforceSatisfaction,
            ExpertAllocation expertAllocation, CoGCBonusType cogcBonusType, Headquarters hq, bool corpHQBonus)
        {
            if (AdvancedBuildingConfiguration.UseEfficiencyOverride)
            {
                Efficiency = AdvancedBuildingConfiguration.EfficiencyOverride / 100;
            }
            else
            {
                var expertBonus = expertAllocation.GetEfficiencyBonus(Building.Expertise);
                var hqBonus = hq.GetFactionEfficiencyFactorForIndustry(Building.Expertise);
                var cogcBonus = GetCoGCBonus(cogcBonusType);
                var corpBonus = corpHQBonus ? 0.1 : 0;
                var satisfaction = 0d;
                satisfaction += workforceSatisfaction.Pioneers * Building.WorkforceRatio.Pioneers;
                satisfaction += workforceSatisfaction.Settlers * Building.WorkforceRatio.Settlers;
                satisfaction += workforceSatisfaction.Technicians * Building.WorkforceRatio.Technicians;
                satisfaction += workforceSatisfaction.Engineers * Building.WorkforceRatio.Engineers;
                satisfaction += workforceSatisfaction.Scientists * Building.WorkforceRatio.Scientists;

                Efficiency = satisfaction * (1 + expertBonus) * (1 + hqBonus) * (1 + cogcBonus) * (1 + corpBonus) * (AdvancedBuildingConfiguration.ProductionLineCondition / 100) * _fertilityBonus;
            }

            if (AvailableRecipes == null)
            {
                return;
            }
            
            foreach (var recipe in AvailableRecipes)
            {
                recipe.UpdateProductionEfficiency(Efficiency);
            }
                        
            foreach (var productionQueueElement in Production)
            {
                productionQueueElement.UpdateDuration();
            }
        }

        private void UpdateProductionPercentages()
        {
            var totalProductionTime = Production.Sum(x => x.OrderSize * x.ActiveRecipe?.DurationInMilliseconds ?? 0);
            
            foreach (var productionQueueElement in Production)
            {
                productionQueueElement.UpdatePercentage(totalProductionTime);
            }
        }

        private const double CoGCExpertiseBonus = 0.25;
        private const double CoGCWorkforceBonus = 0.1;
        [SuppressMessage("ReSharper", "CyclomaticComplexity")]
        private double GetCoGCBonus(CoGCBonusType cogcBonusType)
        {
            return cogcBonusType switch
            {
                CoGCBonusType.None => 0,
                CoGCBonusType.Agriculture => Building.Expertise == IndustryType.Agriculture ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Chemistry => Building.Expertise == IndustryType.Chemistry ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Construction => Building.Expertise == IndustryType.Construction ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Electronics => Building.Expertise == IndustryType.Electronics ? CoGCExpertiseBonus : 0,
                CoGCBonusType.FoodIndustries => Building.Expertise == IndustryType.FoodIndustries ? CoGCExpertiseBonus : 0,
                CoGCBonusType.FuelRefining => Building.Expertise == IndustryType.FuelRefining ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Manufacturing => Building.Expertise == IndustryType.Manufacturing ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Metallurgy => Building.Expertise == IndustryType.Metallurgy ? CoGCExpertiseBonus : 0,
                CoGCBonusType.ResourceExtraction => Building.Expertise == IndustryType.ResourceExtraction ? CoGCExpertiseBonus : 0,
                CoGCBonusType.Pioneers => Building.WorkforceRatio.Pioneers * CoGCWorkforceBonus,
                CoGCBonusType.Settlers => Building.WorkforceRatio.Settlers * CoGCWorkforceBonus,
                CoGCBonusType.Technicians => Building.WorkforceRatio.Technicians * CoGCWorkforceBonus,
                CoGCBonusType.Engineers => Building.WorkforceRatio.Engineers * CoGCWorkforceBonus,
                CoGCBonusType.Scientists => Building.WorkforceRatio.Scientists * CoGCWorkforceBonus,
                _ => throw new ArgumentOutOfRangeException(nameof(cogcBonusType), cogcBonusType, null)
            };
        }

        public override string ToString()
        {
            return Amount + "x" + Building.Ticker;
        }

        public void OnPriceDataUpdate()
        {
            RecalculateBuildingCosts();
            if (AvailableRecipes == null)
            {
                return;
            }
            
            foreach (var recipe in AvailableRecipes)
            {
                recipe.OnPriceDataUpdate();
            }
        }
    }   
}