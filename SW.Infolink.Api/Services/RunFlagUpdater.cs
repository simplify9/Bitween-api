
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
                "pgsql" => $@"UPDATE subscription SET running = true
                        WHERE id = '{id}' and is_running = false
                        RETURNING running",
                "mssql" => $@"UPDATE Subscriptions SET IsRunning = true
                        OUTPUT INSERTED.IsRunning 
                        WHERE Id = '{id}' and IsRunning = false",
                "mysql" => $@"SELECT IsRunning FROM Subscriptions
                         WHERE Id = '{id}' and IsRunning = false
                         FOR UPDATE;
                         UPDATE Subscriptions SET Running = true
                         WHERE Id = '{id}' and IsRunning = false",
                _ => ""
            };
            ; 
            
            var result = await dbContext.Set<Subscription>().FromSqlRaw(sqlUpdate).FirstOrDefaultAsync();

            return result != null;
        }
        
        
    }
}