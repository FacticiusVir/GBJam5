namespace GBJam5.Services
{
    public interface IEntityService
        : IGameService
    {
        Entity CreateEntity();

        void RegisterComponent(EntityComponent component);
    }
}
