using Hishab.Application.Orders;
using Hishab.Domain.Customers;

namespace Hishab.Application.Customers;

/// <summary>Applies the configured risk thresholds (options <c>Risk:*</c>) to a customer's record with this shop.</summary>
public interface ICustomerRiskAssessor
{
    RiskAssessment Assess(CustomerOrderHistory history, bool isBlocked);
}
