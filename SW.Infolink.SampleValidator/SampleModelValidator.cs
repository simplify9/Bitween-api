using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.SampleValidator
{
    class SampleModelValidator : AbstractValidator<SampleModel>
    {
        public SampleModelValidator()
        {
            RuleFor(p => p.Id).NotEmpty().LessThan(10);
            RuleFor(p => p.Name).NotEmpty();
        }
    }
}
