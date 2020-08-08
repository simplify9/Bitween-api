using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Adapters
{
    class Create : ICommandHandler<AdapterConfig>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(AdapterConfig model)
        {
            var entity = new Adapter((AdapterType)model.Type, model.Name, model.DocumentId, model.ServerlessId)
            {
                Properties = model.Properties.ToDictionary(),
                Description = model.Description,
                Timeout = model.Timeout
            };
            dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            await dbContext.Create(entity);
            return entity.Id;
        }

        private class Validater : AbstractValidator<AdapterConfig>
        {
            public Validater()
            {
                RuleFor(p => p.Name).NotEmpty();
                RuleFor(p => p.ServerlessId).NotEmpty();
            }
        }
    }
}
