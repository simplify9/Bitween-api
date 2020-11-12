//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Metadata.Builders;
//using SW.EfCoreExtensions;
//using SW.Infolink.Domain;
//using SW.Infolink.Model;

//namespace SW.Infolink
//{
//    internal static class ModelBuilderExtensions
//    {
//        public static OwnedNavigationBuilder<TOwner, Schedule> BuildSchedule<TOwner>(this OwnedNavigationBuilder<TOwner, Schedule> builder, string table)
//            where TOwner : class
//        {
//            builder.ToTable(table);
//            //builder.Property<int>("Id");
//            //builder.HasKey("Id");
//            builder.Property(p => p.On).HasConversion<long>();
//            builder.Property(p => p.Recurrence).HasConversion<byte>();
//            return builder;
//        }
//    }
//}
