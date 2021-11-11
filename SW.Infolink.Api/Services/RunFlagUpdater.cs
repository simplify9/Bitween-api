
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;

namespace SW.Infolink
{
    public class RunFlagUpdater
    {
        private readonly InfolinkDbContext dbContext;
        private readonly string _dbType;
        

        public RunFlagUpdater(InfolinkDbContext dbContext, InfolinkOptions options)
        {
            this.dbContext = dbContext;
            _dbType = options.DatabaseType;
        }

        public async Task<bool> MarkAsRunning(int id)
        {
            var sqlUpdate = _dbType.ToLower() switch
            {
                "pgsql" => $@"UPDATE infolink.subscription SET is_running = true
                        WHERE id = '{id}' and is_running = false
                        RETURNING is_running",
                "mssql" => $@"UPDATE Subscriptions SET IsRunning = 1
                        OUTPUT INSERTED.IsRunning 
                        WHERE Id = '{id}' and IsRunning = 0",
                "mysql" => $@"SELECT IsRunning FROM Subscriptions
                         WHERE Id = '{id}' and IsRunning = false
                         FOR UPDATE;
                         UPDATE Subscriptions SET IsRunning = true
                         WHERE Id = '{id}' and IsRunning = false",
                _ => ""
            };
            ; 
            
            var results = await dbContext.Set<RunningResult>().FromSqlRaw(sqlUpdate).ToListAsync();

            var result = results.SingleOrDefault();
            
            return result != null;
        }
        
        public async Task MarkAsIdle(int id)
        {
            var sqlUpdate = _dbType.ToLower() switch
            {
                "pgsql" => $@"UPDATE infolink.subscription SET is_running = false
                        WHERE id = '{id}'",
                "mssql" => $@"UPDATE Subscriptions SET IsRunning = 0
                        WHERE Id = '{id}'",
                "mysql" => $@"UPDATE Subscriptions SET IsRunning = false
                         WHERE Id = '{id}'",
                _ => ""
            };
            ; 
            
            await dbContext.Database.ExecuteSqlRawAsync(sqlUpdate);
        }
        
        public class RunningResult
        {
            public bool IsRunning { get; set; }
        }
        
        
    }
}