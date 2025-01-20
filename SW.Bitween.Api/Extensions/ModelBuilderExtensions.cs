//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Metadata.Builders;
//using SW.EfCoreExtensions;
//using SW.Bitween.Domain;
//using SW.Bitween.Model;

//namespace SW.Bitween
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
