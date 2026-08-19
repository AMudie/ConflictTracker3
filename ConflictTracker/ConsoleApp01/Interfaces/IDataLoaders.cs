using ConflictCommon.Classes.DTOs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using ConflictCommon.Classes.DTOs;
using ConflictConsole.Classes;

namespace ConflictConsole.Interfaces
{
    internal interface IPlaceLoader
    {
        Task<GeographicalPlace[]> LoadPlacesAsync(string[] args);
    }

    internal interface IActorLoader
    {
        Task<Actor[]> LoadActorsAsync(string[] args);
    }

    internal interface IEventLoader
    {
        Task<Event[]> LoadEventsAsync(string[] args);
    }

    internal interface IFactLoader
    {
        Task<CIAFactDTO[]> LoadFactsAsync(string[] args);
    }

    internal interface IBorderLoader
    {
        Task<CoordinateDTO[]> LoadBordersAsync(string[] args);
    }
}
