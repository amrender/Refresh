using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using INTEROP.RBBond;
using INTEROP.RBYieldCurve;
using Rabobank.CreditTool.Prices;
using Rabobank.PPQ.Common.BusinessObjects.MarketData;
using Rabobank.PPQ.Common.BusinessObjects.MarketData.Bond.Model;
using Rabobank.PPQ.Common.BusinessObjects.MVVM;
using Rabobank.PPQ.Common.Interfaces;
using Rabobank.PPQ.Common.Interfaces.MarketData.Model;
using Rabobank.PPQ.Common.Quark;

namespace Rabobank.QuarkApps.Apps.CreditTool.ViewModels.Price
{
    public class QuarkPrice
    {
        public string Price
        {
            get
            {
                return JoinPrice();
            }
            set
            {
                ParsePrice(value);
            }
        }

        public double? CleanPrice { get; set; }
        public double? WorkoutPrice { get; set; }
        public DateTime? SettlementDate { get; set; }

        string JoinPrice()
        {
            if (WorkoutPrice.HasValue || SettlementDate.HasValue)
                return string.Format("{0},{1},{2}", CleanPrice.Value, WorkoutPrice.HasValue, SettlementDate.Value);
            return string.Format("{0}", CleanPrice);
        }
        

        void ParsePrice(string price)
        {
            if(string.IsNullOrEmpty(price))
            return;
            var items = price.Split(',');
            int counter = -1;
            double dblValue;
            if (items.Length > ++counter)
            {
                double.TryParse(items[counter], out dblValue);
                CleanPrice = dblValue;
            }
            if (items.Length > ++counter)
            {
                double.TryParse(items[counter], out dblValue);
                WorkoutPrice = dblValue;
            }
            if (items.Length > ++counter)
            {
                DateTime date;
                DateTime.TryParse(items[counter], out date);
                SettlementDate = date;
            }
        }
    }
    public class EditPriceViewModel : ViewModelBase, IDataErrorInfo, IEditSpreadsViewModel
    {
        private readonly IYieldCurve _yieldCurve;

        private static readonly List<string> ValidatedProperties = new List<string>
            {
                "CleanPrice",
                "ASM",
                "ZSpread"
            };

        private QuarkPrice _cleanPrice;
        private double? _zSpread;
        private double? _asm;
        private IBondModel _bond;

        public event EventHandler<EventArgs> SaveSpreadsRequest;

        private string _cleanPriceError;
        private string _zspreadError;
        private string _asmError;

        public EditPriceViewModel(IMarketDataObject model, IYieldCurve yieldCurve)
        {
            _yieldCurve = yieldCurve;
            if (model == null)
            {
                throw new Exception("Bond passed in is null");
            }
            _bond = (IBondModel)model;
            _cleanPrice = new QuarkPrice();
            
        }

        public void UpdatePrice(IPrice spread)
        {
            var price=  ((BondPrice)spread).Price;
            _cleanPrice.CleanPrice = price;
        }

        public double? CleanPriceDouble
        {
            get { return _cleanPrice.CleanPrice; }
        }

        public string CleanPrice
        {
            get { return _cleanPrice.Price; }
            set
            {
                if (_cleanPrice.Price == value)
                    return;
               
                //don't calculate the z spread/asm if the clean price is null
                if(value == null)
                    return;

                //build the bond
                IBond bond = _bond.Build(_yieldCurve);

                _cleanPrice.Price = value;

                IQuarantineQuark quarkIsolator = new QuarkQuarantine();
                try
                {
                    _asm = quarkIsolator.BondAssetSwapMargin(_yieldCurve, bond, null, _cleanPrice.CleanPrice, null, null, null, true);
                }
                catch (Exception e)
                {
                    _asmError = e.Message;
                }
                try
                {
                    _zSpread = quarkIsolator.BondZSpread(_yieldCurve, bond, null, _cleanPrice.Price);
                }
                catch (Exception e)
                {
                    _zspreadError = e.Message;
                }
                OnPropertyChanged("CleanPrice");
                OnPropertyChanged("ZSpread");
                OnPropertyChanged("ASM");
            }
        }

        public double? ZSpread
        {
            get { return _zSpread; }
            set
            {
                if (_zSpread == value)
                    return;
                _zSpread = value;
                if (_zSpread == null)
                    return;
                _zspreadError = string.Empty;
                _asmError = string.Empty;
                IQuarantineQuark quarlIsolator = new QuarkQuarantine();
                //build the bond
                IBond bond = _bond.Build(_yieldCurve);
                try
                {
                    _cleanPrice.CleanPrice = quarlIsolator.BondZSpreadToPrice(_yieldCurve, bond, value.Value, null);
                }
                catch (Exception e)
                {
                    _cleanPrice = null;
                    _cleanPriceError = e.Message;
                }

                try
                {
                    _asm = quarlIsolator.BondAssetSwapMargin(_yieldCurve, bond, _yieldCurve.TodaysDate as DateTime?, _cleanPrice.CleanPrice, null, null, null, true);
                }
                catch (Exception e)
                {
                    _asm = null;
                    _asmError = e.Message;
                }
                OnPropertyChanged("CleanPrice");
                OnPropertyChanged("ZSpread");
                OnPropertyChanged("ASM");
            }
        }

        public double? ASM
        {
            get { return _asm; }
            set { _asm = value; }
        }

        public void Save(object sender, EventArgs e)
        {
            //pass the cleanprice back to the Market data row
            if (SaveSpreadsRequest != null)
                SaveSpreadsRequest(this, new EventArgs());
        }

        public bool IsNew
        {
            get { return false; }
        }

        public bool IsReadOnly { get; set; }
        private ICommand _saveCommand;


        public ICommand SaveCommand
        {
            get
            {
                return _saveCommand ??
                       (_saveCommand = new RelayCommand(param => Save(this, new EventArgs()), param => IsValid)); // Removed the predicate "Pending Changes", since we should always be able to 'Accept' the price, and allow for an audit trail.
            }
        }

        #region IDataErrorInfo Members

        string IDataErrorInfo.this[string propertyName]
        {
            get
            {
                // Dirty the commands registered with CommandManager,
                // such as our Save command, so that they are queried
                // to see if they can execute now.
                return GetValidationError(propertyName);
            }
        }

        string IDataErrorInfo.Error
        {
            get { return null; }
        }

        protected virtual string GetValidationError(string propertyName)
        {
            if (!ValidatedProperties.Contains(propertyName))
                return null;

            string error = null;

            switch (propertyName)
            {
                case "CleanPrice":
                    error = ValidateCleanPrice();
                    break;
                case "ASM":
                    error = ValidateAsm();
                    break;
                case "ZSpread":
                    error = ValidateSpread();
                    break;
            }

            return error;
        }


        private string ValidateCleanPrice()
        {
            if (CleanPrice == null)
                return "Clean Price has not been set";
            return String.IsNullOrEmpty(_cleanPriceError) ? null : _cleanPriceError;
        }

        private string ValidateSpread()
        {
            return String.IsNullOrEmpty(_zspreadError) ? null : _zspreadError;
        }

        private string ValidateAsm()
        {
            return String.IsNullOrEmpty(_asmError) ? null : _asmError;
        }

        public bool IsValid
        {
            get
            {
                foreach (string property in ValidatedProperties)
                    if (GetValidationError(property) != null)
                        return false;
                return true;
            }
        }

        #endregion
    }
}