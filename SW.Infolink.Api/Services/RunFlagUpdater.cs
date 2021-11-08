
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
                "mssql" => $@"UPDATE Subscriptions SET IsRunning = true
                        OUTPUT INSERTED.IsRunning 
                        WHERE Id = '{id}' and IsRunning = false",
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
        
        public class RunningResult
        {
            public bool IsRunning { get; set; }
        }
        
        
    }
}