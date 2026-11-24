using SnVerify.Domain.Product;

namespace SnVerify.Tests.Domain
{
    internal sealed class Yme009RawBuilder
    {
        private Yme009FieldRaw _deviceSn = Yme009FieldRaw.Succeeded("SN1");
        private Yme009FieldRaw _softwareVersion = Yme009FieldRaw.Succeeded("SW-1");
        private Yme009FieldRaw _imei1 = Yme009FieldRaw.Succeeded("IMEI1");
        private Yme009FieldRaw _imei2 = Yme009FieldRaw.Succeeded("IMEI2");
        private Yme009FieldRaw _wifiMac = Yme009FieldRaw.Succeeded("AA:BB");
        private Yme009FieldRaw _productionTest = Yme009FieldRaw.Succeeded("1");
        private Yme009FieldRaw _agingReboot = Yme009FieldRaw.Succeeded("1");
        private Yme009FieldRaw _agingReset = Yme009FieldRaw.Succeeded("1");

        public Yme009RawBuilder DeviceSn(Yme009FieldRaw raw)
        {
            _deviceSn = raw;
            return this;
        }

        public Yme009RawBuilder SoftwareVersion(Yme009FieldRaw raw)
        {
            _softwareVersion = raw;
            return this;
        }

        public Yme009RawBuilder Imei1(Yme009FieldRaw raw)
        {
            _imei1 = raw;
            return this;
        }

        public Yme009RawBuilder Imei2(Yme009FieldRaw raw)
        {
            _imei2 = raw;
            return this;
        }

        public Yme009RawBuilder WifiMac(Yme009FieldRaw raw)
        {
            _wifiMac = raw;
            return this;
        }

        public Yme009RawBuilder ProductionTest(Yme009FieldRaw raw)
        {
            _productionTest = raw;
            return this;
        }

        public Yme009RawBuilder AgingReboot(Yme009FieldRaw raw)
        {
            _agingReboot = raw;
            return this;
        }

        public Yme009RawBuilder AgingReset(Yme009FieldRaw raw)
        {
            _agingReset = raw;
            return this;
        }

        public Yme009RawAttributeSet Build()
        {
            return new Yme009RawAttributeSet(
                _deviceSn,
                _softwareVersion,
                _imei1,
                _imei2,
                _wifiMac,
                _productionTest,
                _agingReboot,
                _agingReset);
        }
    }

    internal static class Yme009AttributeSamples
    {
        public static Yme009InspectionDecision Decide(
            Yme009RawAttributeSet raw,
            string scannedSn = "SN1",
            string targetSoftwareVersion = "SW-1")
        {
            return Yme009InspectionJudge.Judge(
                scannedSn,
                targetSoftwareVersion,
                Yme009AttributeParser.Parse(raw));
        }
    }
}
