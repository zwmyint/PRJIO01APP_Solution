﻿using ToDoList.Application.Interfaces.Repositories;
using ToDoList.Domain.Entities;

namespace ToDoList.Persistence.Repositories
{
    public class CountryRepository : ICountryRepository
    {
        private readonly IGenericRepository<Country> _repository;

        public CountryRepository(IGenericRepository<Country> repository)
        {
            _repository = repository;
        }
    }
}
