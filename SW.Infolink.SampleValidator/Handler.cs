using Newtonsoft.Json;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.SampleValidator
{
    class Handler : IInfolinkValidator
    {
        public Handler()
        {
            //Runner.Expect("Somthing");
        }

        public Task<InfolinkValidatorResult> Validate(XchangeFile xchangeFile)
        {
            var sampleModel = JsonConvert.DeserializeObject<SampleModel>(xchangeFile.Data);
            var sampleModelValidator = new SampleModelValidator();
            var result = sampleModelValidator.Validate(sampleModel);

            return Task.FromResult(new InfolinkValidatorResult(result.Errors.Select(i => new KeyValuePair<string, string>(i.PropertyName, i.ErrorMessage))));
        }
    }
}
