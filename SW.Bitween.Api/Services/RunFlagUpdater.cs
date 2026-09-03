using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;

namespace SW.Bitween
{
    public class RunFlagUpdater
    {
        private readonly BitweenDbContext dbContext;
        private readonly string _dbType;


        public RunFlagUpdater(BitweenDbContext dbContext, BitweenOptions options)
        {
            this.dbContext = dbContext;
            _dbType = options.DatabaseType;
        }

        public async Task<bool> MarkAsRunning(int id)
        {
            var sqlUpdate = _dbType.ToLower() switch
            {
                "pgsql" => @"UPDATE infolink.subscription SET is_running = true
                        WHERE id = {0} and is_running = false
                        RETURNING is_running",
                "mssql" => @"UPDATE Subscriptions SET IsRunning = 1
                        OUTPUT INSERTED.IsRunning 
                        WHERE Id = {0} and IsRunning = 0",
                "mysql" => @"SELECT IsRunning FROM Subscriptions
                         WHERE Id = {0} and IsRunning = false
                         FOR UPDATE;
                         UPDATE Subscriptions SET IsRunning = true
                         WHERE Id = {0} and IsRunning = false",
                _ => ""
            };
          
            var results = await dbContext.Set<RunningResult>().FromSqlRaw(sqlUpdate, id).ToListAsync();

            var result = results.SingleOrDefault();
            // result is null when is running is true
            return result != null;
        }

        public async Task MarkAsIdle(int id)
        {
            var sqlUpdate = _dbType.ToLower() switch
            {
                "pgsql" => @"UPDATE infolink.subscription SET is_running = false
                        WHERE id = {0}",
                "mssql" => @"UPDATE Subscriptions SET IsRunning = 0
                        WHERE Id = {0}",
                "mysql" => @"UPDATE Subscriptions SET IsRunning = false
                         WHERE Id = {0}",
                _ => ""
            };
            ;

            await dbContext.Database.ExecuteSqlRawAsync(sqlUpdate, id);
        }

        public class RunningResult
        {
            public bool IsRunning { get; set; }
        }
    }
}