using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;
using PRUNner.Backend.Data;
using PRUNner.Backend.Data.Components;
using PRUNner.Backend.Enums;
using ReactiveUI;

namespace PRUNner.Backend.BasePlanner
{
    public class PlanetProductionTable : ReactiveObject
    {
        private readonly PlanetaryBase _planetaryBase;
        public PlanetProductionTable(PlanetaryBase planetaryBase)
        {
            _planetaryBase = planetaryBase;
        }

        public ObservableCollection<PlanetProductionRow> Rows { get; } = new();
        public ObservableCollection<PlanetProductionRow> Inputs { get; } = new();
        public ObservableCollection<PlanetProductionRow> Outputs { get; } = new();

        public void Update(IEnumerable<PlanetBuilding> buildings)
        {
            Rows.Clear();
            Inputs.Clear();
            Outputs.Clear();
            
            WorkforceConsumptionFactors.Pioneers.AddUsedConsumables(_planetaryBase.ProvidedConsumables, _planetaryBase.WorkforceRequired.Pioneers, _planetaryBase.WorkforceCapacity.Pioneers, AddInput);
            WorkforceConsumptionFactors.Settlers.AddUsedConsumables(_planetaryBase.ProvidedConsumables, _planetaryBase.WorkforceRequired.Settlers, _planetaryBase.WorkforceCapacity.Settlers, AddInput);
            WorkforceConsumptionFactors.Technicians.AddUsedConsumables(_planetaryBase.ProvidedConsumables, _planetaryBase.WorkforceRequired.Technicians, _planetaryBase.WorkforceCapacity.Technicians, AddInput);
            WorkforceConsumptionFactors.Engineers.AddUsedConsumables(_planetaryBase.ProvidedConsumables, _planetaryBase.WorkforceRequired.Engineers, _planetaryBase.WorkforceCapacity.Engineers, AddInput);
            WorkforceConsumptionFactors.Scientists.AddUsedConsumables(_planetaryBase.ProvidedConsumables, _planetaryBase.WorkforceRequired.Scientists, _planetaryBase.WorkforceCapacity.Scientists, AddInput);
            
            foreach (var building in buildings)
            {
                foreach (var production in building.Production)
                {
                    if (production.ActiveRecipe == null)
                    {
                        // Happens with EXT/COL/RIG when the planet has no resource to extract
                        continue;
                    }
                    
                    foreach (var input in production.ActiveRecipe.Inputs)
                    {
                        var amount = CalculateAmount(building, production, input);
                        AddInput(input.Material, amount);
                    }
                    
                    foreach (var output in production.ActiveRecipe!.Outputs)
                    {
                        var amount = CalculateAmount(building, production, output);
                        AddOutput(output.Material, amount);
                    }
                }
            }
            
            foreach (var row in Rows)
            {
                if (Math.Abs(row.Balance) < 0.1)
                {
                    continue;
                }

                if (row.Balance > 0)
                {
                    Outputs.Add(row);
                }
                else
                {
                    Inputs.Add(row);
                }
            }
            
            this.RaisePropertyChanged(nameof(Rows));
            this.RaisePropertyChanged(nameof(Outputs));
            this.RaisePropertyChanged(nameof(Inputs));
        }

        private double CalculateAmount(PlanetBuilding building, PlanetBuildingProductionQueueElement recipe, MaterialIO input)
        {
            return building.Amount * building.Efficiency * input.DailyAmount * recipe.Percentage * 0.01;
        }

        private void AddInput(MaterialData material, double amount)
        {
            var row = GetRow(material);
            row.Inputs += amount;
        } 
        
        private void AddOutput(MaterialData material, double amount)
        {
            var row = GetRow(material);
            row.Outputs += amount;
        }

        private PlanetProductionRow GetRow(MaterialData material)
        {
            var row = Rows.SingleOrDefault(x => x.Material == material);
            if (row != null)
            {
                return row;
            }
            
            row = new PlanetProductionRow(material);
            Rows.Add(row);
            return row;
        }
        
        private string _currentSortMode = "";
        private void Sort(string sortModeName, SortOrder defaultSortOrder, Func<PlanetProductionRow, object> comparer)
        {
            List<PlanetProductionRow> result;
            if (_currentSortMode.Equals(sortModeName))
            {
                result = defaultSortOrder == SortOrder.Ascending 
                    ? Rows.OrderByDescending(comparer).ToList()
                    : Rows.OrderBy(comparer).ToList();
                _currentSortMode = sortModeName + "Inverse";
            }
            else
            {
                result = defaultSortOrder == SortOrder.Ascending 
                    ? Rows.OrderBy(comparer).ToList()
                    : Rows.OrderByDescending(comparer).ToList();
                _currentSortMode = sortModeName;
            }
            
            Rows.Clear();
            Rows.AddRange(result);
        }
        
        public void SortByTicker() => Sort(nameof(SortByTicker), SortOrder.Ascending, x => x.Material.Ticker);
        public void SortByInputs() => Sort(nameof(SortByInputs), SortOrder.Descending, x => x.Inputs);
        public void SortByOutputs() => Sort(nameof(SortByOutputs), SortOrder.Descending, x => x.Outputs);
        public void SortByBalance() => Sort(nameof(SortByBalance), SortOrder.Descending, x => x.Balance);
        public void SortByValue() => Sort(nameof(SortByValue), SortOrder.Descending, x => x.Value);
    }
}