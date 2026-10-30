using System.Net;
using System.Net.Http.Json;
using UluBilge.Api.Models;
using UluBilge.App.Commands.RegionHandler;
using UluBilge.App.Queries.RegionHandler;
using UluBilge.Core.Entities;

namespace UluBilge.Test;

[TestFixture]
public sealed class RegionControllerTests
{
    private ApiTestClient api = null!;

    [SetUp]
    public async Task SetUp()
    {
        api = new ApiTestClient();
        await api.LoginAsync();
    }

    [TearDown]
    public void TearDown() => api.Dispose();

    [Test]
    public async Task Crud_Add_Update_List_GetById_Delete_InOrder()
    {
        var regionId = Random.Shared.Next(1_000_000, int.MaxValue);
        var created = false;

        try
        {
            // 1. Ekle.
            using var addResponse = await api.Client.PostAsJsonAsync("/v1/Region/Add", new AddRegionAsync.Command
            {
                RegionId = regionId,
                RegionDescription = "Test bölgesi"
            });
            created = addResponse.IsSuccessStatusCode;
            Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var added = await addResponse.Content.ReadFromJsonAsync<BaseResponse<AddRegionAsync.Result>>();
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.Data.Status, Is.True);
            Assert.That(added.Data.RegionId, Is.EqualTo(regionId));

            // 2. Güncelle.
            using var updateResponse = await api.Client.PutAsJsonAsync("/v1/Region/Update", new UpdateRegionAsync.Command
            {
                RegionId = regionId,
                RegionDescription = "Güncellenmiş test bölgesi"
            });
            Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var updated = await updateResponse.Content.ReadFromJsonAsync<BaseResponse<UpdateRegionAsync.Result>>();
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Data.Status, Is.True);

            // 3. Listele ve güncellenmiş kaydı kontrol et.
            Region? listedRegion = null;
            for (var pageNumber = 1; ; pageNumber++)
            {
                using var listResponse = await api.Client.GetAsync($"/v1/Region/GetRegions?pageNumber={pageNumber}&pageSize=100");
                Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var list = await listResponse.Content.ReadFromJsonAsync<BaseResponse<GetRegionsAsync.Result>>();
                Assert.That(list, Is.Not.Null);
                Assert.That(list!.Data.PageNumber, Is.EqualTo(pageNumber));
                Assert.That(list.Data.PageSize, Is.EqualTo(100));
                listedRegion = list.Data.Data.SingleOrDefault(region => region.RegionId == regionId);
                if (listedRegion is not null || (long)pageNumber * 100 >= list.Data.Total) break;
            }
            Assert.That(listedRegion, Is.Not.Null);
            Assert.That(listedRegion!.RegionDescription, Is.EqualTo("Güncellenmiş test bölgesi"));

            // 4. ID ile getir.
            using var getResponse = await api.Client.GetAsync($"/v1/Region/GetRegion/{regionId}");
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var item = await getResponse.Content.ReadFromJsonAsync<BaseResponse<GetRegionAsync.Result>>();
            Assert.That(item, Is.Not.Null);
            Assert.That(item!.Data.Item.RegionId, Is.EqualTo(regionId));
            Assert.That(item.Data.Item.RegionDescription, Is.EqualTo("Güncellenmiş test bölgesi"));

            // 5. Sil ve API üzerinden kaydın artık bulunamadığını doğrula.
            using var deleteResponse = await api.Client.DeleteAsync($"/v1/Region/Delete/{regionId}");
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var deleted = await deleteResponse.Content.ReadFromJsonAsync<BaseResponse<DeleteRegionAsync.Result>>();
            Assert.That(deleted, Is.Not.Null);
            Assert.That(deleted!.Data.Status, Is.True);

            using var missingResponse = await api.Client.GetAsync($"/v1/Region/GetRegion/{regionId}");
            Assert.That(missingResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            created = false;
        }
        finally
        {
            // Ara adımlardan biri başarısız olursa testin eklediği kaydı API ile temizle.
            if (created)
            {
                using var cleanupResponse = await api.Client.DeleteAsync($"/v1/Region/Delete/{regionId}");
                Assert.That(cleanupResponse.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NotFound));
                using var checkResponse = await api.Client.GetAsync($"/v1/Region/GetRegion/{regionId}");
                Assert.That(checkResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
        }
    }
}
