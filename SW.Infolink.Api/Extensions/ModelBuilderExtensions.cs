using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SW.Infolink.Model;

namespace SW.Infolink
{
    public static class ModelBuilderExtensions
    {
        public static OwnedNavigationBuilder<TOwner, Schedule> BuildSchedule<TOwner>(this OwnedNavigationBuilder<TOwner, Schedule> builder, string table)
            where TOwner : class
        {
            builder.ToTable(table);
            builder.Property<int>("Id");
            builder.HasKey("Id");
            builder.Property(p => p.Recurrence).HasColumnName("Recurrence").HasConversion<byte>();
            return builder;
        }
    }
}
